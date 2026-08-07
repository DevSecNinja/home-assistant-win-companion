# Quickstart: Home Assistant Windows Companion (MVP)

## Prerequisites

- Windows 10 (build 19041+) or Windows 11.
- .NET 9 SDK (`dotnet --version` ≥ 9.0).
- A running Home Assistant instance with the `mobile_app` integration loaded
  (included in `default_config`).
- A Home Assistant **long-lived access token**: Home Assistant → your profile →
  Security → *Long-lived access tokens* → *Create token*.

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
3. Paste your long-lived access token.
4. Click **Connect**. The app validates the token, registers this PC as a device,
   and loads your dashboard in the embedded view.

## Verify the user stories

- **US1 (Dashboard)**: Your Home Assistant dashboard is visible and interactive in
  the app window. Close and reopen the app — it reconnects without asking for the
  token again.
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
