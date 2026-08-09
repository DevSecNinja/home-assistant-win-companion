# Code signing decision

## Decision

Release binaries should be Authenticode-signed. The preferred provider is
[SignPath Foundation](https://signpath.org/), using SignPath's GitHub integration
and certificate for eligible open source projects.

[OSSign](https://ossign.org/) is the fallback if SignPath does not accept the
project or cannot support its release format. Buying and operating a private OV/EV
certificate is not justified for the current project.

## Why SignPath

- Free for approved, actively maintained projects under an OSI-approved licence.
- The private key remains in SignPath's managed signing infrastructure.
- Its GitHub connector verifies that artifacts came from the configured repository
  and GitHub-hosted workflow before signing.
- It supports automated Authenticode signing without placing a certificate or
  private key in repository or Actions secrets.
- Every release still requires approval, which is appropriate for a security-
  sensitive companion holding Home Assistant credentials.

The certificate publisher will be **SignPath Foundation**, not the repository owner.

## Eligibility gates

An application should be submitted only after all of these are true:

1. The repository is public. **Complete.**
2. GitHub-hosted CI builds and tests x64 and ARM64 successfully. **Complete.**
3. At least one unsigned release exists in the same format that will later be
   signed.
4. Release artifacts are built entirely from this public repository. **Complete.**
5. Branch protection or rulesets require review and passing CI. **Complete.**
6. The release workflow produces an immutable artifact before requesting signing.

## Release design

The initial signed release remains an unpackaged, zipped x64 and ARM64 application:

1. Build and test on a GitHub-hosted Windows runner.
2. Publish the self-contained release files into a versioned ZIP.
3. Upload the unsigned ZIP as a workflow artifact.
4. Submit that exact artifact to SignPath.
5. Require manual signing approval.
6. Publish only the returned signed artifact and its SHA-256 checksum.

Every direct GitHub Action and reusable workflow reference remains pinned by digest.
The SignPath action must also be pinned before use.

MSIX packaging is a separate deployment decision. It may improve installation,
uninstallation, identity, and Windows capability access, but it should not block
signing the existing release format.

## Code signing policy

Once SignPath accepts the project, release pages and the README will state:

> Free code signing provided by [SignPath.io](https://about.signpath.io),
> certificate by [SignPath Foundation](https://signpath.org).

Roles:

- **Committer/reviewer**: repository owner and approved maintainers.
- **Signing approver**: repository owner.

Privacy statement:

> This program does not transfer information to networked systems other than the
> Home Assistant server and package sources explicitly requested by the user.

## Key handling

No code-signing private key will be generated, downloaded, or stored by this
repository. SignPath holds the key in its managed signing service. The repository
stores only the minimum API token and identifiers needed to submit signing requests,
scoped according to SignPath guidance.

## SmartScreen expectations

Authenticode establishes publisher identity and artifact integrity. It does not
guarantee immediate SmartScreen reputation for a new certificate, so early releases
may still receive reputation warnings while trust accumulates.

Until SignPath onboarding is complete, unsigned Actions artifacts are test builds.
The installation guide requires users to select a successful `main` run and verify
the commit embedded in the artifact name before accepting any SmartScreen prompt.
That provenance check is useful, but it does not provide the publisher identity that
Authenticode will add.

## Fallback

If SignPath Foundation declines the application:

1. Apply to OSSign for open-source Authenticode signing.
2. If OSSign is unsuitable, publish unsigned checksummed archives with explicit
   SmartScreen instructions until project demand justifies a commercial certificate.
