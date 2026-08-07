# Quickstart: Home Assistant Windows Companion (MVP)

## Prerequisites

- Windows 10 (build 19041+) or Windows 11.
- .NET 9 SDK (`dotnet --version` ≥ 9.0).
- A running Home Assistant instance with the `mobile_app` integration loaded
  (included in `default_config`).
- Your Home Assistant account credentials (you'll sign in through your browser —
  no token to create or paste).

## Build & run

```powershell
# From the repository root
dotnet restore HaCompanion.sln
dotnet build HaCompanion.sln -c Debug

# Run the WinUI app
dotnet run --project src/HaCompanion.App/HaCompanion.App.csproj
```

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
- **US2 (Sensors)**: In Home Assistant → Settings → Devices & Services → *Mobile App*,
  a new device for your PC appears with **Battery Level** and **Battery State**
  sensors that update.
- **US3 (Notifications)**: Trigger a `persistent_notification.create` (Developer
  Tools → Services) or an automation that creates a persistent notification. A
  Windows toast appears. Minimize the app to the tray and confirm toasts still work;
  clicking a toast restores the window.

## Run tests

```powershell
dotnet test tests/HaCompanion.Core.Tests/HaCompanion.Core.Tests.csproj
```

## Sign out

Use the tray menu → **Disconnect** (or the app's sign-out action) to clear stored
credentials from the Windows Credential Locker.
