namespace HaCompanion_App;

/// <summary>
/// User-facing product identity. Centralised so the tray tooltip, window title,
/// OAuth browser page and any future surface cannot drift apart.
/// </summary>
/// <remarks>
/// The name deliberately reads "Windows Companion for Home Assistant" rather
/// than leading with the Home Assistant trademark: this is an independent
/// project, and leading with the mark would imply it is an official product.
/// See <c>docs/branding.md</c>.
/// </remarks>
internal static class Branding
{
    /// <summary>Full product name, for window titles and about surfaces.</summary>
    internal const string ProductName = "Windows Companion for Home Assistant";

    /// <summary>
    /// Short form for space-constrained surfaces such as the notification-area
    /// tooltip, which Windows truncates at 127 characters including status text.
    /// </summary>
    internal const string ShortName = "Windows Companion";

    /// <summary>Non-endorsement notice required wherever the product is identified.</summary>
    internal const string TrademarkNotice =
        "An independent project. Not affiliated with, endorsed by, or sponsored by the "
        + "Open Home Foundation, Nabu Casa, or the Home Assistant project.";
}
