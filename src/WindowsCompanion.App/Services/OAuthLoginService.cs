using System.Diagnostics;
using WindowsCompanion.Core.HomeAssistant;
using WindowsCompanion.Core.Models;

namespace WindowsCompanion_App.Services;

/// <summary>
/// Drives the interactive OAuth2 login: opens the Home Assistant authorize page
/// in the user's default browser and captures the returned code on a fixed
/// loopback port, then exchanges it for tokens.
/// </summary>
public sealed class OAuthLoginService
{
    private readonly HttpClient _http;

    public OAuthLoginService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<TokenResponse> SignInAsync(string baseUrl, CancellationToken ct = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl = HaOAuthClient.BuildAuthorizeUrl(
            baseUrl, AppConstants.ClientId, AppConstants.RedirectUri, state);

        var listener = new LoopbackOAuthListener();
        var codeTask = listener.WaitForCodeAsync(AppConstants.LoopbackPort, state, ct);

        OpenBrowser(authorizeUrl);

        var code = await codeTask.ConfigureAwait(false);

        var oauth = new HaOAuthClient(_http, baseUrl);
        return await oauth.ExchangeCodeAsync(code, AppConstants.ClientId, ct).ConfigureAwait(false);
    }

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        });
    }
}
