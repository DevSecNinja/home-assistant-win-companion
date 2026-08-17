# Tasks: Internal and External Home Assistant URLs

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add RouteUrlPolicy enforcing HTTPS for external, rejecting unsafe redirects and captive portals.
- [x] T002 Add RouteProbe with credential-safety ordering: transport → manifest → token → API → webhook identity.
- [x] T003 Add RouteValidator proving same-instance via webhook get_config hass_device_id comparison.
- [x] T004 Add RouteSelector with trust classification from CIDRs, SSIDs, wired rules, and five connection modes.
- [x] T005 Add RouteSupervisor with event-driven evaluation, 5s debounce, 2-min cooldown, and 30s floor.
- [x] T006 Add ConnectionLifecycle with exclusive lease, generation counter, connection-wanted flag, and pre-emption.
- [x] T007 Implement failover: RouteUnhealthy from ConnectionManager triggers candidate probe and route switch.
- [x] T008 Add trusted-CIDR parsing with validation, canonicalization, and overlap detection.
- [x] T009 Add Connection settings panel with separate-URLs opt-in, mode selector, trusted-network editor, and status display.
- [x] T010 Implement migration from single-URL and first dual-URL release configurations.
- [x] T011 Add unit tests for route selection, validation, supervisor timing, and lifecycle serialization.
