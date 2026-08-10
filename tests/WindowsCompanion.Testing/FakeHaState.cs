using System.Collections.Concurrent;
using System.Text.Json;

namespace WindowsCompanion.Testing;

/// <summary>Describes a mobile-app registration observed by the fake server.</summary>
/// <param name="DeviceId">The registered device identifier.</param>
/// <param name="Payload">The registration payload.</param>
/// <param name="Attempt">The device's one-based registration attempt number.</param>
public sealed record FakeHaRegistration(
    string DeviceId,
    JsonElement Payload,
    int Attempt);

/// <summary>Stores mutable protocol state owned by one fake-server scenario.</summary>
public sealed class FakeHaState
{
    private readonly ConcurrentDictionary<string, int> _registrationAttempts =
        new(StringComparer.Ordinal);

    /// <summary>Gets observed registration attempts.</summary>
    public ConcurrentQueue<FakeHaRegistration> Registrations { get; } = new();
    /// <summary>Gets the latest registration payload for each sensor.</summary>
    public ConcurrentDictionary<string, JsonElement> RegisteredSensors { get; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets the latest accepted state payload for each sensor.</summary>
    public ConcurrentDictionary<string, JsonElement> SensorStates { get; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets active WebSocket sessions by session identifier.</summary>
    public ConcurrentDictionary<Guid, FakeHaWebSocketSession> WebSocketSessions { get; } = new();
    /// <summary>Gets notification confirmation identifiers observed from clients.</summary>
    public ConcurrentDictionary<string, byte> ConfirmedNotifications { get; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets refresh tokens revoked during the scenario.</summary>
    public ConcurrentDictionary<string, byte> RevokedRefreshTokens { get; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets webhook identifiers deleted during the scenario.</summary>
    public ConcurrentDictionary<string, byte> DeletedWebhooks { get; } =
        new(StringComparer.Ordinal);

    internal FakeHaRegistration RecordRegistration(string deviceId, JsonElement payload)
    {
        var attempt = _registrationAttempts.AddOrUpdate(deviceId, 1, static (_, count) => count + 1);
        var registration = new FakeHaRegistration(deviceId, payload.Clone(), attempt);
        Registrations.Enqueue(registration);
        return registration;
    }
}
