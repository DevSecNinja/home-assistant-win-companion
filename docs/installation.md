# Installation

The companion currently ships as an unpackaged Windows application. A future WinGet
package is tracked in [issue #39](https://github.com/DevSecNinja/home-assistant-win-companion/issues/39).

## Requirements

- Windows 10 build 19041 or later, or Windows 11.
- x64 Windows for the initial release artifacts.
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
- [Windows App Runtime 2.3](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- A Home Assistant instance with the built-in `mobile_app` integration.

If Windows App Runtime is missing, the application exits during startup, commonly
with `REGDB_E_CLASSNOTREG`.

## Release artifacts

Use artifacts from the repository's
[GitHub Releases](https://github.com/DevSecNinja/home-assistant-win-companion/releases)
page once releases are published.

GitHub Actions pull-request artifacts are explicitly named
`unsigned-windows-x64-<commit>`. They are test builds, not supported releases, and
may contain unreviewed pull-request code.

## Install

1. Download the versioned x64 release ZIP and its SHA-256 checksum.
2. Verify the checksum:

   ```powershell
   Get-FileHash .\HaCompanion-<version>-win-x64.zip -Algorithm SHA256
   ```

3. When signed releases become available, open the executable's **Properties →
   Digital Signatures** page and verify the documented publisher.
4. Extract the ZIP to a permanent user-writable directory, recommended:

   ```text
   %LOCALAPPDATA%\Programs\HaCompanion\
   ```

5. Launch `HaCompanion.App.exe`.
6. Enter the Home Assistant URL and complete sign-in in the browser.
7. Optionally enable **Start with Windows** in the status overview.

Do not run permanently from inside the ZIP, a temporary extraction directory, or a
folder that will be renamed. The startup entry points to the executable's exact
location.

Unsigned builds may trigger Microsoft Defender SmartScreen. Verify that the download
came from this repository before choosing **More info → Run anyway**. Code-signing
plans are documented in [the signing decision](code-signing.md).

## Update

1. Use the tray menu to select **Exit**.
2. Download and verify the new release.
3. Extract it over the existing installation directory.
4. Launch the companion again.

The following user data is outside the application directory and is preserved:

- `%LOCALAPPDATA%\HaCompanion\settings.json`
- `%LOCALAPPDATA%\HaCompanion\logs\`
- OAuth and webhook credentials in Windows Credential Locker

Start with Windows continues to work when the executable path stays the same. If the
directory moves, launch the app manually once; an existing startup entry is repaired
to the current executable path.

## Uninstall

1. Turn off **Start with Windows** in the status overview.
2. Use **Remove server…** if Home Assistant credentials and the Mobile App
   registration should be revoked.
3. Select **Exit** from the tray menu.
4. Delete the application directory.
5. Optionally delete `%LOCALAPPDATA%\HaCompanion\` to remove settings and logs.

If the application was deleted before **Remove server…** was used, reinstall or
temporarily extract the same application, launch it as the same Windows user, and
use **Remove server…**. The Home Assistant Mobile App device can also be removed
from Home Assistant, but this does not by itself remove the local refresh token.

## Start with Windows

The toggle creates a per-user entry under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The command contains only the fully quoted executable path and `--startup`; no URL,
token, webhook ID, or other credential is stored in the command line. Startup occurs
after the user signs in and launches directly into the tray when a saved session is
available.

No Windows Service, scheduled task, administrator elevation, or machine-wide startup
entry is used.
