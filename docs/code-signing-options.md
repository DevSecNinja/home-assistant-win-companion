# Code signing options

**Status:** Draft — no provider selected

## Current release posture

The first releases are unsigned. Each release instead provides:

- SHA-256 checksums for every ZIP and SBOM.
- Per-architecture SPDX JSON SBOMs.
- GitHub build-provenance attestations tied to this repository's release workflow.
- Explicit SmartScreen and verification instructions in
  [the installation guide](installation.md).

These controls establish artifact integrity and workflow provenance, but they do
not give Windows a verified publisher identity. Users should therefore expect an
unknown-publisher SmartScreen warning.

## Goal

Future release executables should carry an Authenticode signature without placing
a long-lived private signing key in the repository or GitHub Actions. The release
workflow should submit an immutable, already-built artifact to a managed signing
service and publish exactly the signed result.

## Options under consideration

### Open-source signing programs

SignPath Foundation and OSSign are the preferred category because they can provide
managed Authenticode signing without the project operating a private key.

Neither program is currently available to this project. Their eligibility,
capacity, and supported onboarding paths may change, so the project should reassess
them before considering a commercial certificate.

### Commercial OV or EV certificate

A commercial certificate would make the project responsible for purchase,
identity validation, renewal, access control, and secure key or HSM operation.
That cost and operational burden are not justified for the current early release.

### Continue unsigned temporarily

Unsigned releases are acceptable only while they remain clearly labelled and keep
checksums, SBOMs, attestations, and conservative installation guidance. This is a
temporary release posture, not a claim that provenance replaces Authenticode.

## Revisit triggers

Re-evaluate signing when any of the following occurs:

- SignPath Foundation or OSSign becomes available to the project.
- Release adoption makes SmartScreen a material installation barrier.
- A trusted sponsor funds managed commercial signing.
- Windows packaging or Store distribution changes the signing requirements.

## Proposed signing flow

If a managed provider becomes available:

1. Build and test x64 and ARM64 artifacts on GitHub-hosted runners.
2. Generate checksums, SBOMs, and build-provenance attestations.
3. Submit those immutable artifacts to the signing provider.
4. Require manual approval for production signing.
5. Publish only the returned signed ZIP contents and matching checksums.

Provider-specific credentials and project identifiers must not be added until a
provider has accepted the project and its threat model has been reviewed.

Tracking issue: [#10](https://github.com/DevSecNinja/home-assistant-win-companion/issues/10).
