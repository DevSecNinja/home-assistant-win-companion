<p align="center">
  <img src="brand/dist/mark-128.png" width="96" height="96" alt="" />
</p>

<h1 align="center">Windows Companion for Home Assistant</h1>

<p align="center">
  A lean, native Windows companion for
  <a href="https://www.home-assistant.io/">Home Assistant</a>.
</p>

> [!NOTE]
> This is an independent project. It is not affiliated with, endorsed by, or
> sponsored by the Open Home Foundation, Nabu Casa, or the Home Assistant project.
> "Home Assistant" is a trademark of the Open Home Foundation. "Windows" is a
> trademark of the Microsoft group of companies.

Windows Companion stays in the system tray, reports opt-in PC sensors to Home
Assistant, shows Home Assistant notifications as native Windows toasts, and opens
the Home Assistant interface in your default browser. It uses Home Assistant's
built-in `mobile_app` integration, so no custom integration or MQTT broker is
required.

## Features

- **Windows sensors in Home Assistant.** Report activity, screen lock, battery,
  network, displays, storage, meeting context, audio devices, theme, updates, and
  more. Every sensor is individually configurable, and privacy-sensitive sensors
  are disabled by default.
- **Useful automation signals.** Turn off desk lights when the PC becomes inactive,
  show an on-air light while the microphone is in use, activate a work scene when
  a second display connects, or send a reminder when disk space runs low.
- **Native Windows notifications.** Receive Home Assistant notifications as
  Windows toasts.
- Optional internal and external Home Assistant URLs with trusted-network routing.
- Browser-based OAuth sign-in with secrets stored in Windows Credential Locker.
- Native tray behavior and optional Start with Windows.
- Demo mode for previewing the app without connecting a Home Assistant server.
- Local connection health, diagnostic logs, and release notices.

The Sensors screen includes an automation idea for each sensor with a useful
state-driven use case. See the [user guide](docs/user-guide.md#automation-ideas)
for more inspiration.

This project intentionally does not embed a dashboard, run commands, provide a
media player, or run while the Windows user is logged out.

## Get started

**End users:** Download the setup package for your PC from
[GitHub Releases](https://github.com/DevSecNinja/home-assistant-win-companion/releases),
then follow the [installation guide](docs/installation.md). See the
[user guide](docs/user-guide.md) for connection modes, sensors, privacy, updates,
and troubleshooting.

Current downloads are unsigned and may trigger Microsoft Defender SmartScreen.
Verify the checksum and build attestation as described in the installation guide.

**Developers:** Full app development requires Windows, the .NET 10 SDK, Windows
App Runtime 2.3, and the matching Windows SDK.

```powershell
.\scripts\run.ps1
.\scripts\test.ps1
```

Use `scripts\run.ps1`, not `dotnet run`, for this unpackaged WinUI application.
See the [developer guide](docs/development.md) and
[contribution guidelines](CONTRIBUTING.md) for architecture, targeted tests, build
commands, and repository conventions.

## Documentation

| Audience | Document |
| --- | --- |
| End users | [User guide](docs/user-guide.md) |
| Installation and updates | [Installation guide](docs/installation.md) |
| Home Assistant configuration | [Examples](examples/home-assistant/) |
| Developers | [Developer guide](docs/development.md) |
| Home Assistant and Windows behavior | [Protocol and platform notes](docs/protocol-notes.md) |
| Contributors | [Contributing](CONTRIBUTING.md) |
| Security reporters | [Security policy](SECURITY.md) |

Feature specifications, research, and historical implementation plans are kept in
[`specs/`](specs/).

## Status

The project is under active development. Browser sign-in, session resume, sensor
reporting, notifications, routing, and release checks are implemented and tested
against Home Assistant behavior. Review the
[changelog](CHANGELOG.md) for release history.

## Credits

The project builds on the work of the
[Home Assistant](https://www.home-assistant.io/) team and the official
[home-assistant/iOS](https://github.com/home-assistant/iOS) companion. Sensor
identifiers and the `Active` sensor design deliberately align with the official
apps. [HASS.Agent](https://github.com/LAB02-Research/HASS.Agent) remains the
established, more feature-rich Windows option.

## License

[MIT](LICENSE) © Jean-Paul van Ravensberg
