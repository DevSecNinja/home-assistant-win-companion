# Tasks: Currently Playing Media

> **Retroactive reconstruction.** Tasks derived from shipped code, not generated
> before implementation. All marked complete.

- [x] T001 Add MediaPlaybackState Core model with session-preference logic preferring Playing over fallback.
- [x] T002 Add per-sensor isolation: only read title/artist/app when media_now_playing is enabled.
- [x] T003 Add MediaSensorSource with SMTC session manager, 2-second poll, and AUMID-to-display-name resolution.
- [x] T004 Attribute formatting with 255-char bounds on title, artist, and app_name.
- [x] T005 Register source in SensorCatalog, add unit tests, and validate build.
