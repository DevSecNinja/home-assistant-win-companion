# Security Policy

## Supported versions

The project is currently pre-release. Security fixes are made on the latest
`main` branch; older commits and development builds are not supported separately.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability or include credentials,
Home Assistant URLs, webhook IDs, logs, or configuration files in a public report.

Use
[GitHub's private vulnerability reporting](https://github.com/DevSecNinja/home-assistant-win-companion/security/advisories/new)
to provide:

- A clear description of the issue and its impact.
- Reproduction steps or a minimal proof of concept.
- The affected version or commit.
- Any suggested mitigation, if known.

You should receive an initial acknowledgement within three business days. Updates
will be provided as the issue is reproduced, assessed, and fixed. Please allow time
for a coordinated release before publishing details.

## Credential exposure

The companion stores OAuth refresh tokens, webhook IDs, and cloudhook URLs in the
Windows Credential Locker. Treat these as secrets.

If any of them may have been exposed:

1. Use **Remove server...** in the companion to revoke the refresh token and remove
   the local registration.
2. Delete the affected Mobile App device in Home Assistant if the webhook ID may
   have been disclosed.
3. Sign in again to create fresh credentials.

The non-secret `%LOCALAPPDATA%\WindowsCompanion\settings.json` file may identify the
Home Assistant server and device. Review it before sharing it privately with a
report.
