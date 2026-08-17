# Feature Specification: Currently Playing Media

**Status**: Shipped

Add opt-in `media_now_playing` and `media_playing` sensors backed by the
Windows System Media Transport Controls (SMTC) session manager
(`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`),
per issue [#160](https://github.com/DevSecNinja/home-assistant-win-companion/issues/160).

## Requirements

- Both sensors are `SensorPrivacy.Sensitive` and default off: media titles and
  artists are personal data, comparable to the camera/microphone-in-use and
  Wi-Fi identifier sensors.
- `media_now_playing` (`sensor`) reports the current track title as its state,
  truncated to 255 characters, with `artist`, `app_name` and
  `playback_status` as attributes. It falls back to the source app's name
  when there is no title, and to `Idle` when there is no active
  session or neither is available.
- `media_playing` (`binary_sensor`) is on only while SMTC reports
  `Playing`; Paused/Stopped/Opened/Changing/Closed all report off.
- Session selection prefers whichever SMTC session is actively `Playing`
  over `GetCurrentSession()`'s arbitrary most-recently-activated session, so
  a paused/backgrounded player (e.g. Spotify left paused) cannot mask media
  genuinely playing elsewhere (e.g. a browser tab). `GetCurrentSession()` is
  used only as a fallback when no session is playing.
- Capture is scoped to whichever sensor id(s) are enabled/permitted: when
  only `media_playing` is enabled, no title, artist, or source app is ever
  fetched or resolved - only playback status is read. Enabling the binary
  sensor alone cannot leak Now Playing metadata, matching the per-sensor
  isolation the codebase's `SensorPreviewGate` guarantees elsewhere.
- The source app is resolved from its AUMID (`SourceAppUserModelId`) to a
  friendly display name via `Windows.ApplicationModel.AppInfo`; resolution is
  best-effort (OS below Windows 10 2004, an uninstalled app, or a denied
  lookup) and falls back to the raw AUMID rather than failing the read.
- `artist` and `app_name` attributes are bounded to 255 characters (the same
  limit applied to the title state) so a misbehaving media source cannot
  balloon the webhook payload or the recorder.
- Reads the active SMTC session on a 2-second poll while at least one of the
  two sensors is enabled, and makes zero SMTC calls otherwise, per the
  `SensorCatalog` start/stop contract. Only a change to the reading pushes an
  extra Home Assistant update.
