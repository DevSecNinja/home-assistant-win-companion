# Tasks: Demo Mode

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add DemoSession model in Core tracking active state, preventing writes and server contact.
- [x] T002 Add "Explore in demo mode" entry point on sign-in screen bypassing OAuth.
- [x] T003 Wire demo session into AppController so no webhook, WebSocket, or sensor push runs.
- [x] T004 Show persistent warning banner on every screen with leave-demo action.
- [x] T005 Hide server-only actions (Open HA, Connection, Update now, Disconnect, Remove server) during demo.
- [x] T006 Ensure signing in or resuming a saved session ends the demo and discards demo catalog state.
- [x] T007 Add unit tests verifying demo isolation and exit behavior.
