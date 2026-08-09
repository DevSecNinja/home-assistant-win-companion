namespace HaCompanion.Core.Models;

/// <summary>
/// How the companion picks between the internal and external Home Assistant
/// addresses. Both addresses must point at the same instance.
/// </summary>
public enum ConnectionMode
{
    /// <summary>Pick the best address for the current network, with fallback.</summary>
    Automatic,

    /// <summary>Try the internal address first, fall back to the external one.</summary>
    PreferInternal,

    /// <summary>Try the external address first, fall back to the internal one.</summary>
    PreferExternal,

    /// <summary>Never leave the internal address, even when it is unreachable.</summary>
    InternalOnly,

    /// <summary>Never use the internal address.</summary>
    ExternalOnly
}

/// <summary>Which of the two configured addresses a connection uses.</summary>
public enum RouteKind
{
    Internal,
    External
}

/// <summary>
/// What the user sees about routing. <see cref="Unassigned"/> covers installs
/// migrated from a single URL that have not yet been classified by the user.
/// </summary>
public enum RouteStatus
{
    Offline,
    Internal,
    External,
    FailingOver,
    Unassigned
}
