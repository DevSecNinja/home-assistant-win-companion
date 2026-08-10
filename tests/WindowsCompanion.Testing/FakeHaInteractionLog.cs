using System.Text.Json;

namespace WindowsCompanion.Testing;

/// <summary>Records sanitized scenario interactions and supports deterministic waits.</summary>
public sealed class FakeHaInteractionLog
{
    private readonly object _gate = new();
    private readonly List<FakeHaInteraction> _interactions = [];
    private readonly List<Waiter> _waiters = [];
    private readonly Func<IReadOnlyCollection<string>> _sensitiveValues;
    private long _sequence;

    internal FakeHaInteractionLog(Func<IReadOnlyCollection<string>> sensitiveValues)
    {
        _sensitiveValues = sensitiveValues;
    }

    /// <summary>Records and returns a sanitized interaction.</summary>
    public FakeHaInteraction Record(
        FakeHaInteractionKind kind,
        string method,
        string pathOrMessageType,
        object? payload = null,
        string outcome = "Success",
        string? correlationId = null)
    {
        var sanitizedPath = Redact(pathOrMessageType);
        var sanitizedPayload = Sanitize(payload);
        FakeHaInteraction interaction;
        List<Waiter> completed;
        lock (_gate)
        {
            interaction = new FakeHaInteraction(
                ++_sequence,
                DateTimeOffset.UtcNow,
                kind,
                method,
                sanitizedPath,
                correlationId,
                sanitizedPayload,
                outcome);
            _interactions.Add(interaction);
            completed = _waiters
                .Where(waiter => interaction.Sequence > waiter.AfterSequence
                                 && waiter.Predicate(interaction))
                .ToList();
            foreach (var waiter in completed) _waiters.Remove(waiter);
        }

        foreach (var waiter in completed) waiter.Completion.TrySetResult(interaction);
        return interaction;
    }

    /// <summary>Returns a stable snapshot of interactions in observation order.</summary>
    public IReadOnlyList<FakeHaInteraction> Snapshot()
    {
        lock (_gate) return _interactions.ToArray();
    }

    /// <summary>Waits until a matching interaction is observed.</summary>
    public Task<FakeHaInteraction> WaitForAsync(
        Func<FakeHaInteraction, bool> predicate,
        CancellationToken cancellationToken = default,
        long afterSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            var existing = _interactions.FirstOrDefault(interaction =>
                interaction.Sequence > afterSequence && predicate(interaction));
            if (existing is not null) return Task.FromResult(existing);

            var waiter = new Waiter(predicate, afterSequence);
            _waiters.Add(waiter);
            waiter.Cancellation = cancellationToken.Register(() =>
            {
                lock (_gate) _waiters.Remove(waiter);
                waiter.Completion.TrySetCanceled(cancellationToken);
            });
            return AwaitAndDisposeAsync(waiter);
        }
    }

    /// <summary>Waits up to the specified duration for a matching interaction.</summary>
    public async Task<FakeHaInteraction> WaitForAsync(
        Func<FakeHaInteraction, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        long afterSequence = 0)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);
        try
        {
            return await WaitForAsync(predicate, linked.Token, afterSequence).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for a fake Home Assistant interaction.{Environment.NewLine}{FormatHistory()}");
        }
    }

    /// <summary>Formats a concise chronological interaction history.</summary>
    public string FormatHistory() => string.Join(
        Environment.NewLine,
        Snapshot().Select(interaction =>
            $"{interaction.Sequence}: {interaction.Kind} {interaction.Method} "
            + $"{interaction.PathOrMessageType} => {interaction.Outcome}"));

    private async Task<FakeHaInteraction> AwaitAndDisposeAsync(Waiter waiter)
    {
        try
        {
            return await waiter.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            waiter.Cancellation.Dispose();
        }
    }

    private JsonElement? Sanitize(object? payload)
    {
        if (payload is null) return null;
        var json = JsonSerializer.Serialize(payload);
        json = Redact(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private string Redact(string value)
    {
        foreach (var sensitive in _sensitiveValues())
        {
            if (!string.IsNullOrEmpty(sensitive))
                value = value.Replace(sensitive, "[REDACTED]", StringComparison.Ordinal);
        }

        return value;
    }

    private sealed class Waiter(
        Func<FakeHaInteraction, bool> predicate,
        long afterSequence)
    {
        public Func<FakeHaInteraction, bool> Predicate { get; } = predicate;
        public long AfterSequence { get; } = afterSequence;
        public TaskCompletionSource<FakeHaInteraction> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation { get; set; }
    }
}
