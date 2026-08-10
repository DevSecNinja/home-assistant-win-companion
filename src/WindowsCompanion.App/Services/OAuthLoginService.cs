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
    private readonly IUriLauncher _uriLauncher;

    public OAuthLoginService(HttpClient http, IUriLauncher? uriLauncher = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _uriLauncher = uriLauncher ?? new ShellUriLauncher();
    }

    public async Task<TokenResponse> SignInAsync(string baseUrl, CancellationToken ct = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var authorizeUrl = HaOAuthClient.BuildAuthorizeUrl(
            baseUrl, AppConstants.ClientId, AppConstants.RedirectUri, state);

        using var listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var listener = new LoopbackOAuthListener();
        var codeTask = listener.WaitForCodeAsync(
            AppConstants.LoopbackPort, state, listenerCancellation.Token);

        string code;
        try
        {
            await _uriLauncher.LaunchAsync(authorizeUrl, ct).ConfigureAwait(false);
            code = await codeTask.ConfigureAwait(false);
        }
        finally
        {
            if (!codeTask.IsCompleted)
            {
                listenerCancellation.Cancel();
                try
                {
                    await codeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (listenerCancellation.IsCancellationRequested)
                {
                }
            }
        }

        var oauth = new HaOAuthClient(_http, baseUrl);
        return await oauth.ExchangeCodeAsync(code, AppConstants.ClientId, ct).ConfigureAwait(false);
    }

}
