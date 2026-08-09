using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsCompanion.Core.App;

namespace WindowsCompanion.Core.Lifecycle;

/// <summary>
/// The last lifecycle transition this installation observed, and whether Home
/// Assistant is known to have received it.
/// </summary>
public sealed record LifecycleRecord
{
    public LifecycleTransition Transition { get; init; } = LifecycleTransition.Running;

    public DateTimeOffset ObservedAt { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool Critical { get; init; }

    /// <summary>
    /// True only once a sensor batch containing this transition was accepted by
    /// Home Assistant. It is never set optimistically: an unacknowledged record is
    /// the entire point of the journal.
    /// </summary>
    public bool Acknowledged { get; init; }
}

/// <summary>Where the last observed transition survives a shutdown.</summary>
public interface ILifecycleJournal
{
    LifecycleRecord? Read();

    void Write(LifecycleRecord record);
}

/// <summary>
/// Stores the journal as its own small JSON file next to settings.json.
/// </summary>
/// <remarks>
/// Deliberately a separate file rather than a field in the settings: it is written
/// while Windows is shutting down, when the process can be terminated mid-write. A
/// truncated file must cost nothing more than a forgotten transition - it must not
/// be able to take the server configuration, sensor choices or the pointer to the
/// stored credentials down with it.
///
/// Nothing here throws. A failing journal must never delay or break app exit, so
/// every operation degrades to "we do not know what happened", which is a state the
/// startup recovery already has to handle for power loss anyway.
/// </remarks>
public sealed class FileLifecycleJournal : ILifecycleJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public FileLifecycleJournal(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            AppDataPaths.Resolve(),
            "lifecycle.json");
    }

    public string Path => _path;

    public LifecycleRecord? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<LifecycleRecord>(File.ReadAllText(_path), Options);
        }
        catch
        {
            return null;
        }
    }

    public void Write(LifecycleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(record, Options));
        }
        catch
        {
            // Best effort: see the remarks above.
        }
    }
}
