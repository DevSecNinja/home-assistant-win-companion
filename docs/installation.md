# Installation

The companion ships as a per-user Windows installer, with a portable ZIP as an
alternative. A future WinGet package is tracked in
[issue #39](https://github.com/DevSecNinja/home-assistant-win-companion/issues/39).

The product and executable are named **WindowsCompanion** and
`WindowsCompanion.exe`.

It is an independent project and is not affiliated with, endorsed by, or sponsored by
the Open Home Foundation, Nabu Casa, or the Home Assistant project.

## Requirements

- Windows 10 build 19041 or later, or Windows 11.
- x64 or ARM64 Windows. Download the artifact matching **Settings → System →
  About → System type**.
- [Windows App Runtime 2.3](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- A Home Assistant instance with the built-in `mobile_app` integration.

Versioned Releases are self-contained and do not require a separate .NET install.
Source builds and CI test artifacts require the .NET 9 Desktop Runtime.

If Windows App Runtime is missing, the application exits during startup, commonly
with `REGDB_E_CLASSNOTREG`.

## Release artifacts

Prefer a versioned setup executable from
[GitHub Releases](https://github.com/DevSecNinja/home-assistant-win-companion/releases)
matching your architecture. Portable ZIPs are provided for users who deliberately
want a manual, no-installer deployment.

Until the first Release is published, a test build can be downloaded from GitHub
Actions:

1. Open the repository's [CI workflow](https://github.com/DevSecNinja/home-assistant-win-companion/actions/workflows/ci.yml).
2. Select a successful run on the `main` branch for the commit you intend to test.
3. In **Artifacts**, download `unsigned-windows-x64-<commit>` or
   `unsigned-windows-arm64-<commit>`.
4. Confirm the commit in the artifact name matches the run's commit. GitHub may
   require you to sign in before downloading an Actions artifact.

Pull-request artifacts may contain unreviewed code and are not supported releases.
Do not download an artifact merely because its workflow succeeded.

## Install with setup.exe (recommended)

1. Download the setup package for the correct architecture:

   - `WindowsCompanion-<version>-win-x64-setup.zip` for Intel and AMD PCs.
   - `WindowsCompanion-<version>-win-arm64-setup.zip` for Windows on ARM PCs.

2. Download its `.sha256` file and verify the setup package. The computed value
   must match the published checksum:

   ```powershell
   Get-FileHash .\WindowsCompanion-<version>-win-<architecture>-setup.zip `
     -Algorithm SHA256
   ```

   Actions test artifacts do not currently have a separately published checksum;
   verify their `main` workflow run and commit as described above instead.

   Each architecture also includes a matching `.spdx.json` Software Bill of
   Materials (SBOM) and checksum. It describes the application payload shared by
   setup and portable packages.

   GitHub also records build-provenance attestations for each ZIP and SBOM. With the
   [GitHub CLI](https://cli.github.com/) installed, verify a downloaded asset was
   produced by this repository's workflow:

   ```powershell
   gh attestation verify `
     .\WindowsCompanion-<version>-win-<architecture>-setup.zip `
     --repo DevSecNinja/home-assistant-win-companion
   ```

3. Unblock and extract the setup ZIP, then run the `-setup.exe` inside it. Keep the
   two `.bin` files beside the executable while setup runs. This loader-free package
   avoids creating an unsigned temporary executable that managed Defender ASR
   policies commonly block.

4. Setup installs without administrator rights under:

   ```text
   %LOCALAPPDATA%\Programs\WindowsCompanion\
   ```

5. Complete the wizard, then launch the companion from the Start Menu or the final
   setup page.
6. Enter the Home Assistant URL and complete sign-in in the browser.
7. Optionally enable **Start with Windows** in the status overview.

Setup also creates an **Apps & Features** uninstall entry. It does not enable Start
with Windows automatically. If the companion is running during an upgrade, exit it
from the tray and run setup again.

## Use the portable ZIP

1. Download `WindowsCompanion-<version>-win-<architecture>.zip` and verify its
   checksum and provenance as above.
2. Right-click the ZIP, choose **Properties**, and select **Unblock** if shown.
3. Extract the single versioned folder to a permanent user-writable location.
4. Launch `WindowsCompanion.exe` from that extracted folder.

Do not run permanently from Downloads, inside the ZIP, a temporary extraction
directory, or a folder that will be renamed. Start with Windows points to the
executable's exact location.

Current builds are unsigned and may show **Windows protected your PC** from
Microsoft Defender SmartScreen. Only after verifying the repository, workflow run,
commit, and architecture above, choose **More info**, confirm the app is
`HaCompanion.App.exe` with an unknown publisher, and choose **Run anyway**. A managed
or organizational Windows policy may prohibit unsigned apps; do not weaken that
policy to install this test build.

Unsigned means Windows cannot cryptographically identify the publisher. A checksum
or successful Actions run helps detect a changed download, but is not a substitute
for an Authenticode signature.

## Why builds are not signed yet

The project intends to Authenticode-sign future releases through a managed provider,
so no private signing key has to be stored in this repository or GitHub Actions.
The available free open-source signing programs are not currently an option for this
project, and buying and securely operating a commercial certificate is not justified
for this early release. Until a suitable provider becomes available, builds remain
explicitly labelled unsigned. The longer-term decision remains tracked in
[issue #10](https://github.com/DevSecNinja/home-assistant-win-companion/issues/10).

## Update

1. Use the tray menu to select **Exit**.
2. Download and verify the new release.
3. For an installed copy, run the newer setup executable; it upgrades in place.
4. For a portable copy, replace the old extracted application folder.
5. Launch the companion again.

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
   connection should be revoked. This clears the saved sign-in and local settings,
   but Home Assistant's app API cannot delete its Mobile App device entry.
3. Select **Exit** from the tray menu.
4. For an installed copy, use **Settings → Apps → Installed apps →
   WindowsCompanion → Uninstall**. For a portable copy, delete its
   extracted application folder.
5. Optionally delete `%LOCALAPPDATA%\HaCompanion\` to remove settings and logs;
   normal uninstall deliberately preserves these for upgrades/reinstallation.

If the application was deleted before **Remove server…** was used, reinstall or
temporarily extract the same application, launch it as the same Windows user, and
use **Remove server…**. The Home Assistant Mobile App device can also be removed
manually under **Settings → Devices & services → Mobile App**. Removing it there
does not by itself remove the local refresh token; use both actions for complete
cleanup.

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
