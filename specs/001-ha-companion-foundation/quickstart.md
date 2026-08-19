# Quickstart: Home Assistant Windows Companion

## Prerequisites

- Windows 10 (build 19041+) or Windows 11.
- .NET 9 SDK (`dotnet --version` ≥ 9.0).
- The **Windows App Runtime 2.3** must be installed (the app ships unpackaged and
  uses the Windows App SDK bootstrapper). If it is missing, the app exits at
  startup with `REGDB_E_CLASSNOTREG`. The MSIX packages ship inside the
  `Microsoft.WindowsAppSDK.Runtime` NuGet package under
  `tools/MSIX/win10-x64/` and can be installed with `Add-AppxPackage`.
- A running Home Assistant instance with the `mobile_app` integration loaded
  (included in `default_config`).
- Your Home Assistant account credentials (you'll sign in through your browser —
  no token to create or paste).

## Build & run

```powershell
# From the repository root: builds and launches what it just built
.\scripts\run.ps1
```

> `dotnet run` is **not** supported for this project: it resolves the .NET root to
> the app's own output folder and fails with a misleading "You must install or
> update .NET" dialog even when the runtime is present. Note also that a failed
> launch does not exit - the apphost stays alive showing an error dialog titled
> `WindowsCompanion.exe` - so a running process is not proof that the app started.

> WinUI 3 apps may require the Windows App SDK runtime; unpackaged runs use the
> bootstrapper referenced by the app project.

## First-time setup (in the app)

1. On launch, the **Connect** view appears.
2. Enter your Home Assistant base URL (e.g. `https://homeassistant.local:8123`).
3. Click **Sign in**. Your default browser opens the Home Assistant login page.
4. Log in and approve the request. The browser redirects back to the app, which
   registers this PC as a device and shows the lean **Status** view.

## Verify the user stories

- **US1 (Connect & open HA)**: After signing in, the Status view shows the
  connection state and battery. Click **Open Home Assistant** to launch your
  instance in the default browser. Close and reopen the app — it resumes the
  session without asking you to sign in again.
- **US2 (Sensors)**: Open **Sensors…** in the companion. Enable or disable a sensor,
  change the idle threshold, and use **Update now**. In Home Assistant → Settings →
  Devices & Services → *Mobile App*, a device for your PC appears with the enabled
  battery, activity, lock, network and diagnostic entities.
- **US3 (Notifications)**: In Home Assistant, call the `notify.mobile_app_<your_pc>`
  action (or target the PC's notify entity from `notify.send_message`) with a
  title and message. A Windows toast appears. Minimize the app to the tray and
  confirm toasts still work; clicking a toast restores the window.

  > If your PC registered before this app declared push support, it appears under
  > `notify.mobile_app_<your_pc>` immediately but only shows up under
  > `notify.send_message` after you reload the Mobile App integration once
  > (Settings → Devices & Services → Mobile App → ⋮ → Reload) or restart HA.

## Run tests

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj
```

## Pause or remove the connection

Use **Disconnect** to pause reporting and notifications without deleting the saved
server or credentials; the same action becomes **Reconnect**. Use **Remove server…**
to revoke the refresh token and delete the local configuration and Credential
Locker entries.

## Disabled sensor entities

Turning a sensor off disables its Home Assistant entity but does not delete the
entity-registry entry. It may remain greyed out under "+N entities not shown" on
the device page and will not appear in normal pickers. Home Assistant does not
offer deletion while the Mobile App integration still provides the entity. The
only complete removal is deleting the whole Mobile App device, which invalidates
the app registration and requires signing in again.
