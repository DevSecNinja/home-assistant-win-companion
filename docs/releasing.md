# Releasing

Release automation produces unsigned draft release candidates until the SignPath
integration described in [the code-signing decision](code-signing.md) is active.

## Test a release candidate

Run the **Release candidate** workflow manually with a SemVer such as `0.1.0`. This:

- runs the Core coverage gates;
- publishes the framework-dependent x64 application;
- creates a versioned ZIP and SHA-256 file;
- uploads an unsigned workflow artifact;
- does not create a tag or GitHub Release.

## Create a draft release

1. Confirm `main` is green.
2. Create and push a signed SemVer tag:

   ```powershell
   git switch main
   git pull --ff-only
   git tag -s v0.1.0 -m "release: v0.1.0"
   git push origin v0.1.0
   ```

3. The workflow tests and packages that exact tag.
4. It creates a **draft** GitHub Release containing:
   - `HaCompanion-<version>-win-x64.zip`
   - `HaCompanion-<version>-win-x64.zip.sha256`
5. Download and smoke-test the draft artifact using
   [the pre-release checklist](pre-release-smoke-test.md).
6. Do not publish the draft until its signing and release notes are acceptable.

## Future signing

When SignPath is active, insert signing after packaging and before draft-release
upload. Only the returned signed artifact should be attached to the publishable
release. The unsigned workflow artifact may remain available to maintainers for
diagnostics but must stay clearly labelled.

## Future WinGet submission

WinGet publication is tracked in
[issue #39](https://github.com/DevSecNinja/home-assistant-win-companion/issues/39).
Manifest automation must consume the final signed and published GitHub Release URL,
never a pull-request artifact or unsigned draft candidate.
