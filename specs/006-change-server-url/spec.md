# Feature Specification: Change Server URL

**Status**: Shipped

Users can replace the URL used to reach the same Home Assistant instance without
losing the refresh token, webhook registration, entities, or history.

## Requirements

- Normalize the candidate URL and resolve redirects before validation.
- Prove the existing refresh token and webhook registration work at the candidate.
- Commit only after validation; invalid/unreachable URLs leave the old session intact.
- Reconnect WebSocket, sensor sync, and push notifications against the new URL.
- If credentials are rejected, offer a confirmed replace-server sign-in flow.
- Never register a new device for a same-instance address change.
