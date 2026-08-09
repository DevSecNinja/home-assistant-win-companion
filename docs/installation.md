# Installation

The companion currently ships as an unpackaged Windows application. A future WinGet
package is tracked in [issue #39](https://github.com/DevSecNinja/home-assistant-win-companion/issues/39).

## Requirements

- Windows 10 build 19041 or later, or Windows 11.
- x64 or ARM64 Windows. Download the artifact matching **Settings → System →
  About → System type**.
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
- [Windows App Runtime 2.3](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
- A Home Assistant instance with the built-in `mobile_app` integration.

If Windows App Runtime is missing, the application exits during startup, commonly
with `REGDB_E_CLASSNOTREG`.

## Release artifacts

The companion is an unpackaged application distributed as a ZIP, not an installer.
Prefer a versioned ZIP from
[GitHub Releases](https://github.com/DevSecNinja/home-assistant-win-companion/releases)
once one is available.

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

## Install an unsigned build

1. Download the ZIP for the correct architecture:

   - `win-x64` for Intel and AMD Windows PCs.
   - `win-arm64` for Windows on ARM PCs.

2. For a versioned GitHub Release, also download its `.sha256` file and verify the
   ZIP. The computed value must match the published checksum:

   ```powershell
   Get-FileHash .\HaCompanion-<version>-win-<architecture>.zip -Algorithm SHA256
   ```

   Actions test artifacts do not currently have a separately published checksum;
   verify their `main` workflow run and commit as described above instead.

   Each Release also includes a matching `.spdx.json` Software Bill of Materials
   (SBOM) and checksum. The SBOM lists the components detected in that architecture's
   package; it is useful for dependency and vulnerability review but is not required
   to run the app.

   GitHub also records build-provenance attestations for each ZIP and SBOM. With the
   [GitHub CLI](https://cli.github.com/) installed, verify a downloaded asset was
   produced by this repository's workflow:

   ```powershell
   gh attestation verify .\HaCompanion-<version>-win-<architecture>.zip `
     --repo DevSecNinja/home-assistant-win-companion
   ```

3. Right-click the downloaded ZIP, choose **Properties**, and select **Unblock** if
   Windows shows that option. Apply the change before extracting so Windows does
   not mark every extracted file separately.

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
