# Installation

The companion ships as a per-user Windows installer, with a portable ZIP as an
alternative. A future WinGet package is tracked in
[issue #39](https://github.com/DevSecNinja/home-assistant-win-companion/issues/39).

The product's display name is **Windows Companion for Home Assistant**. Its
distribution identity and executable are **WindowsCompanion** and
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
Source builds and CI test artifacts require the .NET 10 Desktop Runtime.

If Windows App Runtime is missing, the application exits during startup, commonly
with `REGDB_E_CLASSNOTREG`.

## Release artifacts

Prefer a versioned setup package from
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
`WindowsCompanion.exe` with an unknown publisher, and choose **Run anyway**. A managed
or organizational Windows policy may prohibit unsigned apps; do not weaken that
policy to install this test build.

Unsigned means Windows cannot cryptographically identify the publisher. A checksum
or successful Actions run helps detect a changed download, but is not a substitute
for an Authenticode signature.

### If Defender ASR blocks the app

Microsoft Defender's **Use advanced protection against ransomware** ASR rule
(`C1DB55AB-C21A-4637-BB3F-A12568109D35`) can block a new unsigned
`WindowsCompanion.exe` until Microsoft cloud reputation or publisher trust exists.
This is different from the normal SmartScreen prompt and may appear as **This
operation is blocked by your administrator**.

First verify the release checksum and GitHub attestation exactly as described above.
Only for that verified asset:

- On a personally managed PC, open **Windows Security → Protection history** and
  use **Allow on device** if Windows offers that action.
- On an organization-managed PC, contact the administrator. Microsoft documents a
  per-ASR-rule exclusion or a file-hash allow indicator for reviewed applications.
  The user might not have permission to allow it.

Do not disable Defender, turn off the ransomware rule, or add a broad exclusion for
the user-writable install directory. The loader-free setup package avoids a separate
temporary-executable ASR block, but it cannot give the unsigned application publisher
trust. A future Authenticode-signed release is the long-term fix.

## Why builds are not signed yet

The project intends to Authenticode-sign future releases through a managed provider,
so no private signing key has to be stored in this repository or GitHub Actions.
The available free open-source signing programs are not currently an option for this
project, and buying and securely operating a commercial certificate is not justified
for this early release. Until a suitable provider becomes available, builds remain
explicitly labelled unsigned. The longer-term decision remains tracked in
[issue #10](https://github.com/DevSecNinja/home-assistant-win-companion/issues/10).

## Update

Official release builds check GitHub Releases once, in the background, each time the
application process starts. When a newer stable release exists, Windows shows one
toast with the installed and available versions and a **View release** action. The
notification-area icon also changes for that run: its companion circle becomes a
larger red badge containing `1`, and the tray menu offers **Install update…**. That
command opens or restores the companion and shows the **Update available** banner;
**View release** opens the exact GitHub release page in the browser.

Before an update is known, the tray instead offers **Check for updates…**. It opens
and focuses the companion, immediately shows **Checking for updates…**, and then
shows whether the app is current, an update is available, or the check failed. The
banner also offers **Recheck for updates**. Double-clicking the tray icon opens,
restores, and focuses the companion without changing the update state.

The check never downloads, installs, or restarts the application. Drafts and
prereleases are ignored. If GitHub is unavailable or returns an unusable response,
startup and Home Assistant connectivity continue normally. Automatic failures are
written to the local diagnostic log; failures from a tray check are also shown in
the banner. Source builds, pull-request builds, and ordinary CI artifacts do not
make the automatic request because they are not official versioned releases.

1. Download and verify the new release.
2. For an installed copy, run the newer setup package; it upgrades in place. If
   any installed or source-built companion is running, Setup asks permission to
   close it gracefully. Setup waits up to 15 seconds for sensor, connection, tray,
   window, and process teardown before replacing files. A failed or refused close
   cancels Setup; it never force-terminates or automatically restarts the application.
3. For a portable copy, use the tray menu to select **Exit**, then replace the old
   extracted application folder.
4. Launch the companion again.

The following user data is outside the application directory and is preserved:

- `%LOCALAPPDATA%\WindowsCompanion\settings.json`
- `%LOCALAPPDATA%\WindowsCompanion\logs\`
- OAuth and webhook credentials in Windows Credential Locker

Start with Windows continues to work when the executable path stays the same. If the
directory moves, launch the app manually once; an existing startup entry is repaired
to the current executable path.

### Updating from a build older than the rename

Earlier builds shipped as `HaCompanion.App.exe` and stored data under
`%LOCALAPPDATA%\HaCompanion\`. The first launch of a renamed build migrates what it
can:

- the data directory is moved to `%LOCALAPPDATA%\WindowsCompanion\`, keeping
  settings, logs and the lifecycle journal
- Credential Locker entries found under the old `HaCompanion` resource are re-saved
  under `WindowsCompanion`
- a `HaCompanion` startup registry value is replaced by a `WindowsCompanion` one the
  next time **Start with Windows** is toggled

**Expect to sign in once after upgrading.** The Credential Locker scopes entries to
the calling application, so a renamed executable may not be able to read credentials
written by the old one; the fallback above only helps when it can. Signing in again
is harmless: the device id is kept in `settings.json`, which does migrate, so Home
Assistant updates the existing device rather than adding a second one.

The old executable is not removed for you. Delete the previous
`HaCompanion.App.exe` and its directory after confirming the new build starts and
reconnects.

## Uninstall

1. Turn off **Start with Windows** in the status overview.
2. Use **Remove server…** if Home Assistant credentials and the Mobile App
   connection should be revoked. This clears the saved sign-in and local settings,
   but Home Assistant's app API cannot delete its Mobile App device entry.
3. For an installed copy, use **Settings → Apps → Installed apps →
   WindowsCompanion → Uninstall**. If the companion is running, the uninstaller
   asks permission to close it gracefully and waits up to 15 seconds for complete
   process teardown. A failed or refused close cancels uninstall without forcing
   termination. For a portable copy, select **Exit** from the tray menu and delete
   its extracted application folder.
4. Optionally delete `%LOCALAPPDATA%\WindowsCompanion\` to remove settings and logs;
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
