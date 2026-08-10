using System.Text.Json;

namespace WindowsCompanion.Testing;

/// <summary>Identifies the protocol surface represented by a recorded interaction.</summary>
public enum FakeHaInteractionKind
{
    /// <summary>An OAuth authorization request.</summary>
    Authorization,
    /// <summary>An OAuth token request.</summary>
    Token,
    /// <summary>A Home Assistant REST API request.</summary>
    Api,
    /// <summary>A mobile-app registration request.</summary>
    Registration,
    /// <summary>A mobile-app webhook request.</summary>
    Webhook,
    /// <summary>A WebSocket connection or message.</summary>
    WebSocket,
    /// <summary>A pushed notification or confirmation.</summary>
    Notification
}

/// <summary>Describes one sanitized interaction observed by the fake server.</summary>
/// <param name="Sequence">The scenario-local sequence number.</param>
/// <param name="Timestamp">The UTC observation time.</param>
/// <param name="Kind">The protocol surface.</param>
/// <param name="Method">The HTTP method or message direction.</param>
/// <param name="PathOrMessageType">The sanitized path or message type.</param>
/// <param name="CorrelationId">An optional correlation identifier.</param>
/// <param name="Payload">The sanitized payload, when present.</param>
/// <param name="Outcome">The recorded outcome.</param>
public sealed record FakeHaInteraction(
    long Sequence,
    DateTimeOffset Timestamp,
    FakeHaInteractionKind Kind,
    string Method,
    string PathOrMessageType,
    string? CorrelationId,
    JsonElement? Payload,
    string Outcome);
