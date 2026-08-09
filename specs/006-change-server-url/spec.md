# Feature Specification: Change Server URL

**Status**: Superseded by [008-dual-ha-urls](../008-dual-ha-urls/spec.md)

Users can replace the URL used to reach the same Home Assistant instance without
losing the refresh token, webhook registration, entities, or history.

The single "Change server URL" dialog this describes has been replaced by the
Connection settings panel, which edits an internal and an external address
together. The same-instance guarantee below still holds, and is now proved
through Home Assistant's own device-registry id rather than through a successful
API call alone.

## Requirements

- Normalize the candidate URL and resolve redirects before validation.
- Prove the existing refresh token and webhook registration work at the candidate.
- Commit only after validation; invalid/unreachable URLs leave the old session intact.
- Reconnect WebSocket, sensor sync, and push notifications against the new URL.
- If credentials are rejected, offer a confirmed replace-server sign-in flow.
- Never register a new device for a same-instance address change.
