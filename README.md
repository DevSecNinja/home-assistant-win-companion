# Home Assistant Windows Companion

A lean, native Windows companion for [Home Assistant](https://www.home-assistant.io/) —
the Windows counterpart to the official [iOS/macOS app](https://github.com/home-assistant/iOS).

It is **not** a dashboard. It stays out of your way in the system tray, reports your
PC's status to Home Assistant, turns Home Assistant notifications into native Windows
toasts, and opens Home Assistant in your normal browser when you want the UI.

## Why this project exists

Windows is the one major platform without a first-party Home Assistant companion.
There are good third-party options, but they generally ask you to run extra
infrastructure or install a custom integration. The goals here are deliberately
narrow:

- **Lightweight.** A tray app that does a few things well. No embedded browser, no
  media player, no command runner. If you want the Home Assistant UI, it opens the
  one you already have.
- **Native.** WinUI 3 / Windows App SDK, so it looks and behaves like a Windows app -
  Fluent styling, Mica, native toasts, a real tray icon.
- **App push like macOS/iOS.** Notifications arrive over the same
  `mobile_app` local push channel the official Apple apps use, so the PC shows up as
  a normal notify target in Home Assistant. No MQTT broker, no custom component.
- **Battle tested.** An explicit goal rather than a claim: spec-driven development,
  a platform-agnostic core covered by unit tests, protocol behaviour verified against
  Home Assistant's own source, and decisions recorded in
  [`specs/`](specs/) so the reasoning survives.

Concretely, that means it talks to the **built-in `mobile_app` integration** - the
same one the official companion apps use. Nothing to install on the Home Assistant
side.

### How it compares

[HASS.Agent](https://github.com/LAB02-Research/HASS.Agent) is the established
Windows companion and is far more capable than this project. If you want commands,
quick actions, a media player or a large sensor catalogue today, use it. This table
is about *fit*, not quality.

| | This project | HASS.Agent |
| --- | --- | --- |
| Home Assistant side | Built-in `mobile_app` integration | Custom [HASS.Agent integration](https://github.com/LAB02-Research/HASS.Agent-Integration) (HACS) |
| Extra infrastructure | None | MQTT broker |
| Notifications | `mobile_app` local push, same as the macOS/iOS apps | MQTT + custom integration; supports images and actionable buttons |
| Sign-in | OAuth2 in your browser, no token to paste | Long-lived token + MQTT credentials |
| UI framework | WinUI 3 (Windows App SDK), .NET 9 | WinForms + Syncfusion, .NET 6 |
| Home Assistant UI | Opens your default browser | Built-in WebView |
| Sensors | Small, curated, opt-in per sensor | ~37 built in |
| Commands / media player / quick actions | Not offered | Yes |
| Runs when logged out | No | Yes, via satellite service |
| Maturity | Early; MVP | Mature, large community |

The short version: **HASS.Agent is the featureful option; this aims to be the boring
one that just works with stock Home Assistant.**

## Features (MVP)

- **Browser-based sign-in** — OAuth2 (IndieAuth) with a loopback redirect. No
  long-lived tokens to create or paste. The refresh token is stored in the Windows
  Credential Locker.
- **Tray-resident** — closing the window hides it to the notification area. The tray
  tooltip shows current health. The status overview can register the app to start
  in the tray when the current Windows user signs in.
- **Windows toasts** — notifications sent to this PC from Home Assistant appear as
  native toasts, delivered over the `mobile_app` local push channel (Windows has no
  APNS/FCM equivalent).
- **Opt-in sensor catalog** — battery, active/idle, screen locked, connection type,
  IPv4/IPv6 address, LAN MAC address, Wi-Fi SSID/BSSID, OS version, last boot,
  notification/presentation state, microphone
  and camera use, audio output, headset presence, WinGet update count, system
  lifecycle state, and an
  optional frontmost-app/last-update value. Each sensor can be switched on or off
  individually, shows a local preview, and privacy-sensitive ones are off by
  default. Network identifiers are only read once you enable their own sensor —
  the preview shows nothing beforehand — and the IPv4, IPv6 and MAC readings all
  describe the adapter carrying the active route rather than a VPN or Hyper-V
  adapter.
- **Lifecycle signals** — sleep, sign-out and shutdown are detected without polling
  and pushed as an opt-in `system_state` sensor with a short, strictly bounded final
  attempt. Windows may terminate the app first, so anything undelivered is recorded
  locally and reported after the next successful connection. The companion never
  blocks or delays a shutdown. The sensor is off by default and asks you to confirm
  its limits before it starts, because they cannot be engineered away — see
  [docs/windows-lifecycle-signals.md](docs/windows-lifecycle-signals.md).
- **Health and logs** — a health verdict based on whether the app is actually
  reporting on schedule, plus a rolling local log you can open from the UI.
- **Open Home Assistant** — one click (window or tray menu) to open your instance in
  the default browser.

## Requirements

- Windows 10 (build 19041+) or Windows 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) to run
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build from source
- **Windows App Runtime 2.3** — the app ships unpackaged and uses the Windows App SDK
  bootstrapper. Without it the app exits at startup with `REGDB_E_CLASSNOTREG`. The
  MSIX packages ship inside the `Microsoft.WindowsAppSDK.Runtime` NuGet package under
  `tools/MSIX/win10-x64/` and can be installed with `Add-AppxPackage`.
- A Home Assistant instance with the `mobile_app` integration (part of
  `default_config`)

See the [end-user installation guide](docs/installation.md) for release downloads,
runtime setup, updates, Start with Windows, and uninstallation.

The optional **WinGet Updates** sensor uses Microsoft's
`Microsoft.WinGet.Client` PowerShell module version 1.29.280 or newer. If it is
missing, the app provides a copyable current-user installation command but never
downloads or installs executable code itself. Only the update count is sent to Home
Assistant; package names and versions remain in the local preview.

## Build and run

```powershell
# Build and launch in one step (the supported way to run from source)
.\scripts\run.ps1

.\scripts\test.ps1
.\scripts\test.ps1 -Coverage
```

Coverage is measured for `HaCompanion.Core` only. The current gates are 85% line
and 70% branch coverage; the WinUI/P/Invoke shell remains intentionally thin and
outside the unit-test project.

`scripts/run.ps1` builds and then launches exactly what it just built. That matters:
a solution build and a project build otherwise select different platforms and write
to different folders, which makes it easy to build one binary and silently run an
older one. The script pins the platform and verifies the app actually started.

Two things worth knowing if you build by hand:

- `dotnet run` does **not** work for this project. It resolves the .NET root to the
  app's own output folder and fails with a misleading *"You must install or update
  .NET"* dialog even when the runtime is installed.
- A failed launch does not exit. The .NET apphost stays alive showing an error
  dialog whose window title is `HaCompanion.App.exe`, so "a process is running" is
  not evidence that the app started.

On first launch, enter your Home Assistant URL and click **Sign in** — your browser
opens for login, then the app registers this PC and connects.

## Architecture

| Project | Purpose |
| --- | --- |
| `src/HaCompanion.Core` | Platform-agnostic logic: HA REST/webhook client, OAuth, WebSocket protocol, sensors, reconnect. No UI dependency and covered by unit tests. |
| `src/HaCompanion.App` | WinUI 3 (Windows App SDK) shell: OAuth loopback listener, tray icon, toasts, Credential Locker, battery via `GetSystemPowerStatus`. |
| `tests/HaCompanion.Core.Tests` | xUnit tests for the core library. |

Secrets live only in the Windows Credential Locker, including the refresh token,
`webhook_id`, and any cloudhook URL. Non-secret config (base URL, device id, sensor
choices, and registered-sensor metadata) goes to
`%LOCALAPPDATA%\HaCompanion\settings.json`. The last observed lifecycle transition
is journalled separately in `%LOCALAPPDATA%\HaCompanion\lifecycle.json`, so a write
interrupted by a shutdown cannot damage the configuration. Existing installs migrate
a previously stored plaintext webhook id into the Credential Locker automatically.

## Notes on the Home Assistant APIs used

A few behaviours are easy to get wrong and are worth calling out:

- **Loopback OAuth needs a fixed port.** Home Assistant validates that the refresh
  grant's `client_id` matches the one used at authorization. Since `client_id` *is*
  the loopback redirect URL, an ephemeral port would silently break token refresh.
- **Use HTTPS.** HTTP redirects rewrite `POST` as `GET`, so a `POST /auth/token` sent
  to an `http://` URL that redirects arrives as a `GET` and fails with
  `405 Method Not Allowed`.
- **Notifications use the local push channel.** Home Assistant does *not* fire a
  `persistent_notification` event on the event bus (it uses an internal dispatcher
  signal), so `subscribe_events` for it never fires. Registering with
  `app_data.push_websocket_channel = true` is what makes the PC a notify target;
  notifications then arrive over `mobile_app/push_notification_channel` and must be
  acknowledged within 10s.
- **Disabled sensors remain in Home Assistant.** Switching a sensor off disables its
  entity; it does not delete the entity-registry entry. Home Assistant shows it
  greyed out, usually under "+N entities not shown", and excludes it from normal
  pickers. This is expected and harmless. Sensors removed by a later app version
  are retired the same way. Removing one entirely requires deleting the whole
  Mobile App device, which invalidates the registration and forces this app to
  register again.

See [`specs/001-ha-companion-mvp/contracts/`](specs/001-ha-companion-mvp/contracts/)
for the full API contracts.

## Spec-driven development

This project uses [GitHub Spec Kit](https://github.com/github/spec-kit) where the
size and uncertainty of a feature justify the full workflow. Specifications,
research, API contracts and historical implementation plans live in
[`specs/`](specs/), and the project principles in
[`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Status

MVP, verified against a live Home Assistant instance: browser sign-in, secure
session resume, selectable sensors, health reporting, and notifications arriving
as Windows toasts.

## Credits

- The [Home Assistant](https://www.home-assistant.io/) team.
- [home-assistant/iOS](https://github.com/home-assistant/iOS) — the official
  macOS/iOS companion. Sensor identifiers and the `Active` sensor's design are
  deliberately mirrored from it so entities line up with the official apps.
- [HASS.Agent](https://github.com/LAB02-Research/HASS.Agent) — the established
  Windows companion, and a useful reference for what a Windows sensor catalogue can
  cover.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the Windows development environment,
repository conventions, and pull request expectations.

## License

[MIT](LICENSE) © Jean-Paul van Ravensberg

## Security

Report vulnerabilities privately according to [SECURITY.md](SECURITY.md). Do not
include Home Assistant credentials, URLs, configuration, or logs in a public issue.
