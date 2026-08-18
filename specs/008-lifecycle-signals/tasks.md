# Tasks: Lifecycle Signals

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Define LifecycleSignal enum and LifecycleTransition model with timestamp, reason, and critical flag.
- [x] T002 Add LifecycleTracker state machine with idempotent deduplication of overlapping Windows messages.
- [x] T003 Add LifecycleJournal for local persistence of transitions before delivery attempt.
- [x] T004 Add LifecycleCoordinator: one final push with 2-second timeout on worker thread, never vetoing shutdown.
- [x] T005 Add LifecycleSensorSource exposing system_state sensor with advisory dialog on first enable.
- [x] T006 Add WindowsLifecycleSignalSource observing WM_POWERBROADCAST, WM_QUERYENDSESSION, WM_ENDSESSION, and SystemEvents.
- [x] T007 Implement replay of unacknowledged transitions via attributes after next successful connection.
- [x] T008 Release hook when sensor is disabled; cancel pending push on resume.
- [x] T009 Add unit tests for tracker deduplication, journal persistence, and coordinator timeout behavior.
