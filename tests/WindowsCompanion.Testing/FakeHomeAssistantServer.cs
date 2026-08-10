using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace WindowsCompanion.Testing;

/// <summary>Hosts the loopback Kestrel implementation of the tested Home Assistant surface.</summary>
public sealed class FakeHomeAssistantServer : IAsyncDisposable
{
    private readonly FakeHaScenario _scenario;
    private readonly WebApplication _application;
    private readonly CancellationTokenSource _lifetime = new();

    private FakeHomeAssistantServer(
        FakeHaScenario scenario,
        WebApplication application)
    {
        _scenario = scenario;
        _application = application;
    }

    internal static async Task<FakeHomeAssistantServer> StartAsync(
        FakeHaScenario scenario,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FakeHomeAssistantServer).Assembly.FullName,
            EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        var server = new FakeHomeAssistantServer(scenario, app);
        server.MapEndpoints();
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.SingleOrDefault()
                      ?? throw new InvalidOperationException("Kestrel did not expose its loopback address.");
        scenario.BaseUrl = new Uri(address.EndsWith('/') ? address : address + "/");
        return server;
    }

    /// <summary>Sends a notification to each subscribed WebSocket client.</summary>
    public async Task SendNotificationAsync(
        string title,
        string message,
        string? confirmationId = null,
        CancellationToken cancellationToken = default)
    {
        var sessions = _scenario.State.WebSocketSessions.Values.ToArray();
        foreach (var session in sessions)
        {
            await session.SendNotificationAsync(
                title, message, confirmationId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Closes all active WebSocket connections.</summary>
    public async Task CloseWebSocketsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = _scenario.State.WebSocketSessions.Values.ToArray();
        foreach (var session in sessions)
            await session.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    private void MapEndpoints()
    {
        _application.UseWebSockets();

        _application.MapGet(
            "/auth/authorize",
            (Func<HttpContext, Task<IResult>>)AuthorizeAsync);
        _application.MapPost(
            "/auth/token",
            (Func<HttpContext, Task<IResult>>)TokenAsync);
        _application.MapGet(
            "/api/",
            (Func<HttpContext, Task<IResult>>)ApiRootAsync);
        _application.MapGet(
            "/api/config",
            (Func<HttpContext, Task<IResult>>)ApiConfigAsync);
        _application.MapPost(
            "/api/mobile_app/registrations",
            (Func<HttpContext, Task<IResult>>)RegisterAsync);
        _application.MapPost(
            "/api/webhook/{webhookId}",
            (Func<HttpContext, string, Task<IResult>>)WebhookAsync);
        _application.Map("/api/websocket", WebSocketAsync);
    }

    private async Task<IResult> AuthorizeAsync(HttpContext context)
    {
        await _scenario.Faults
            .WaitIfHeldAsync(FakeHaFaultPoint.Authorization, context.RequestAborted)
            .ConfigureAwait(false);

        var clientId = context.Request.Query["client_id"].ToString();
        var redirectText = context.Request.Query["redirect_uri"].ToString();
        var state = context.Request.Query["state"].ToString();
        var valid = context.Request.Query["response_type"] == "code"
                    && Uri.TryCreate(clientId, UriKind.Absolute, out var client)
                    && client.IsLoopback
                    && !string.IsNullOrWhiteSpace(state)
                    && Uri.TryCreate(redirectText, UriKind.Absolute, out var redirect)
                    && redirect.Scheme == Uri.UriSchemeHttp
                    && redirect.IsLoopback;

        _scenario.Interactions.Record(
            FakeHaInteractionKind.Authorization,
            "GET",
            "/auth/authorize",
            new { client_id = clientId, redirect_uri = redirectText, state },
            valid ? "Success" : "Rejected");
        if (!valid) return Results.BadRequest(new { error = "invalid_request" });

        var destination = QueryHelpers.AddQueryString(
            redirectText,
            new Dictionary<string, string?>
            {
                ["code"] = _scenario.AuthorizationCode,
                ["state"] = state
            });
        return Results.Redirect(destination);
    }

    private async Task<IResult> TokenAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var action = form["action"].ToString();
        if (action == "revoke")
        {
            var token = form["token"].ToString();
            if (!string.IsNullOrEmpty(token))
                _scenario.State.RevokedRefreshTokens.TryAdd(token, 0);
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Token,
                "POST",
                "revoke",
                new { token });
            return Results.Ok();
        }

        var grantType = form["grant_type"].ToString();
        var faultPoint = grantType == "refresh_token"
            ? FakeHaFaultPoint.Refresh
            : FakeHaFaultPoint.TokenExchange;
        await _scenario.Faults
            .WaitIfHeldAsync(faultPoint, context.RequestAborted)
            .ConfigureAwait(false);

        var validClient = Uri.TryCreate(
            form["client_id"].ToString(),
            UriKind.Absolute,
            out var clientId)
            && clientId.IsLoopback;
        var rejected = !validClient || grantType switch
        {
            "authorization_code" =>
                _scenario.Faults.RejectAuthorizationCode
                || form["code"].ToString() != _scenario.AuthorizationCode,
            "refresh_token" =>
                _scenario.Faults.RejectRefreshToken
                || form["refresh_token"].ToString() != _scenario.RefreshToken
                || _scenario.State.RevokedRefreshTokens.ContainsKey(_scenario.RefreshToken),
            _ => true
        };

        _scenario.Interactions.Record(
            FakeHaInteractionKind.Token,
            "POST",
            grantType,
            form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()),
            rejected ? "Rejected" : "Success");
        if (rejected)
            return Results.BadRequest(new { error = "invalid_grant" });

        return grantType == "authorization_code"
            ? Results.Json(new
            {
                access_token = _scenario.AccessToken,
                token_type = "Bearer",
                refresh_token = _scenario.RefreshToken,
                expires_in = 1800
            })
            : Results.Json(new
            {
                access_token = _scenario.AccessToken,
                token_type = "Bearer",
                expires_in = 1800
            });
    }

    private async Task<IResult> ApiRootAsync(HttpContext context)
    {
        if (_scenario.Faults.ApiUnavailable)
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Api,
                "GET",
                "/api/",
                outcome: "Unavailable");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var rejected = !await AuthorizeApiAsync(context).ConfigureAwait(false);
        _scenario.Interactions.Record(
            FakeHaInteractionKind.Api,
            "GET",
            "/api/",
            outcome: rejected ? "Rejected" : "Success");
        return rejected
            ? Results.Unauthorized()
            : Results.Json(new { message = "API running." });
    }

    private async Task<IResult> ApiConfigAsync(HttpContext context)
    {
        if (_scenario.Faults.ApiUnavailable)
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Api,
                "GET",
                "/api/config",
                outcome: "Unavailable");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var rejected = !await AuthorizeApiAsync(context).ConfigureAwait(false);
        _scenario.Interactions.Record(
            FakeHaInteractionKind.Api,
            "GET",
            "/api/config",
            outcome: rejected ? "Rejected" : "Success");
        return rejected
            ? Results.Unauthorized()
            : Results.Json(new
            {
                internal_url = _scenario.BaseUrl,
                external_url = (string?)null,
                version = "2026.8.0-test"
            });
    }

    private async Task<IResult> RegisterAsync(HttpContext context)
    {
        if (_scenario.Faults.ApiUnavailable)
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Registration,
                "POST",
                "/api/mobile_app/registrations",
                outcome: "Unavailable");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (!await AuthorizeApiAsync(context).ConfigureAwait(false))
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Registration,
                "POST",
                "/api/mobile_app/registrations",
                outcome: "Rejected");
            return Results.Unauthorized();
        }
        await _scenario.Faults
            .WaitIfHeldAsync(FakeHaFaultPoint.Registration, context.RequestAborted)
            .ConfigureAwait(false);

        if (_scenario.Faults.MobileAppUnavailable)
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Registration,
                "POST",
                "/api/mobile_app/registrations",
                outcome: "Rejected");
            return Results.NotFound();
        }

        using var document = await JsonDocument
            .ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);
        var required = new[]
        {
            "device_id", "app_id", "app_name", "app_version", "device_name",
            "manufacturer", "model", "os_name", "os_version", "app_data"
        };
        if (required.Any(property => !document.RootElement.TryGetProperty(property, out _))
            || !document.RootElement.TryGetProperty("device_id", out var deviceIdElement)
            || string.IsNullOrWhiteSpace(deviceIdElement.GetString()))
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Registration,
                "POST",
                "/api/mobile_app/registrations",
                document.RootElement,
                "Rejected");
            return Results.BadRequest(new { error = "invalid_registration" });
        }

        var deviceId = deviceIdElement.GetString()!;
        var registration = _scenario.State.RecordRegistration(deviceId, document.RootElement);
        _scenario.Interactions.Record(
            FakeHaInteractionKind.Registration,
            "POST",
            "/api/mobile_app/registrations",
            new { device_id = deviceId, attempt = registration.Attempt });
        return Results.Json(new
        {
            webhook_id = _scenario.WebhookId,
            secret = (string?)null,
            cloudhook_url = (string?)null,
            remote_ui_url = (string?)null
        });
    }

    private async Task<IResult> WebhookAsync(HttpContext context, string webhookId)
    {
        await _scenario.Faults
            .WaitIfHeldAsync(FakeHaFaultPoint.Webhook, context.RequestAborted)
            .ConfigureAwait(false);
        if (_scenario.Faults.ApiUnavailable)
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Webhook,
                "POST",
                "/api/webhook/{redacted}",
                outcome: "Unavailable");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (_scenario.Faults.UnknownWebhook
            || !string.Equals(webhookId, _scenario.WebhookId, StringComparison.Ordinal))
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Webhook,
                "POST",
                "/api/webhook/{redacted}",
                outcome: "Unknown");
            return Results.Text(string.Empty);
        }
        if (_scenario.State.DeletedWebhooks.ContainsKey(webhookId))
        {
            _scenario.Interactions.Record(
                FakeHaInteractionKind.Webhook,
                "POST",
                "/api/webhook/{redacted}",
                outcome: "Gone");
            return Results.StatusCode(StatusCodes.Status410Gone);
        }

        using var document = await JsonDocument
            .ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
        _scenario.Interactions.Record(
            FakeHaInteractionKind.Webhook,
            "POST",
            type ?? "unknown",
            root);

        return type switch
        {
            "update_registration" => Results.Json(new { }),
            "register_sensor" => RegisterSensor(root),
            "update_sensor_states" => UpdateSensorStates(root),
            "get_config" => Results.Json(new
            {
                hass_device_id = _scenario.InstanceDeviceId,
                version = "2026.8.0-test",
                remote_ui_url = (string?)null,
                cloudhook_url = (string?)null
            }),
            _ => Results.BadRequest(new { error = "unknown_webhook_command" })
        };
    }

    private async Task WebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await using var session = new FakeHaWebSocketSession(_scenario, socket);
        _scenario.State.WebSocketSessions[session.Id] = session;
        await session.RunAsync(_lifetime.Token).ConfigureAwait(false);
    }

    private IResult RegisterSensor(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("unique_id", out var uniqueIdElement)
            || string.IsNullOrWhiteSpace(uniqueIdElement.GetString()))
            return Results.BadRequest(new { error = "unique_id_required" });

        _scenario.State.RegisteredSensors[uniqueIdElement.GetString()!] = data.Clone();
        return Results.Json(new { });
    }

    private IResult UpdateSensorStates(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return Results.BadRequest(new { error = "sensor_array_required" });

        var results = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var sensor in data.EnumerateArray())
        {
            var uniqueId = sensor.TryGetProperty("unique_id", out var id)
                ? id.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(uniqueId)) continue;

            if (string.Equals(
                    uniqueId,
                    _scenario.Faults.RejectSensorUniqueId,
                    StringComparison.Ordinal))
            {
                results[uniqueId] = new
                {
                    success = false,
                    error = new { code = "invalid_format", message = "Configured rejection" }
                };
            }
            else if (!_scenario.State.RegisteredSensors.ContainsKey(uniqueId))
            {
                results[uniqueId] = new
                {
                    success = false,
                    error = new { code = "not_registered", message = "Sensor is not registered" }
                };
            }
            else if (_scenario.State.RegisteredSensors[uniqueId]
                     .TryGetProperty("disabled", out var disabled)
                     && disabled.ValueKind == JsonValueKind.True)
            {
                results[uniqueId] = new
                {
                    success = false,
                    error = new { code = "not_registered", message = "Sensor is disabled" }
                };
            }
            else
            {
                _scenario.State.SensorStates[uniqueId] = sensor.Clone();
                results[uniqueId] = new { success = true };
            }
        }

        return Results.Json(results);
    }

    private async Task<bool> AuthorizeApiAsync(HttpContext context)
    {
        await _scenario.Faults
            .WaitIfHeldAsync(FakeHaFaultPoint.Api, context.RequestAborted)
            .ConfigureAwait(false);
        var authorization = context.Request.Headers.Authorization.ToString();
        return string.Equals(
            authorization,
            $"Bearer {_scenario.AccessToken}",
            StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try
        {
            await CloseWebSocketsAsync().ConfigureAwait(false);
            await _application.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            await _application.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
    }
}
