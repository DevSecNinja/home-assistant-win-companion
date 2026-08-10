# Research: Meeting Context Sensors

## Decision: Poll Windows notification state

`SHQueryUserNotificationState` returns the current shell notification state and is
available to desktop apps from `Shell32.dll`. Windows sends no notification when a
full-screen application starts or stops, so the source polls every 10 seconds and
pushes only when the mapped state changes.

Alternatives considered: listening only for `WM_SETTINGCHANGE` misses full-screen
transitions; application-specific meeting integration excludes other clients.

## Decision: Read capability access history without requesting device access

Microphone and camera use are inferred from Windows capability access-history
records under both per-user and machine-wide ConsentStore roots, including
`NonPackaged` descendants. Any entry with a non-positive `LastUsedTimeStop` is
active. Missing or inaccessible records are treated as inactive so one sensor
cannot break synchronization.

The shared capability source samples once per second while either sensor is
enabled. It requests a sensor sync only when the combined microphone/camera
snapshot changes, keeping unchanged polls local.

Alternatives considered: opening the devices would require consent and interfere
with the application already using them; Teams log parsing no longer works.

## Decision: Use vendor-neutral Windows audio endpoint enumeration

The app uses Windows device enumeration for audio render/capture endpoints and the
platform's default audio-render identifier. Friendly names supply `audio_output`;
a conservative name classifier identifies headset/headphone/earbud-class endpoints.
No vendor SDK or additional package is required.

Alternatives considered: Jabra's current browser SDK is inaccessible to WinUI;
native vendor SDKs exclude other hardware; HID telephony state is unreliable because
softphones commonly claim exclusive device access.

## Decision: Add asynchronous source previews

Existing sources can preview synchronously, but device enumeration is asynchronous.
`ISensorSource` therefore gains a default asynchronous preview operation while
`SensorCatalog` and the Sensors page await it. Existing sources keep their current
read path; the audio source refreshes directly without starting its background
poller or transmitting values.

Alternatives considered: blocking WinRT calls on the UI thread risks deadlock and
poor responsiveness; starting disabled sources solely for preview violates the
zero-cost enablement rule.

## Decision: One poller per related source

Notification state, capability use, and audio equipment each have a separate source.
The catalog starts a source when its first sensor becomes enabled and stops it after
its last sensor becomes disabled. Sources cache the last snapshot and request an
immediate sync only after a change.

Alternatives considered: one global poller keeps disabled sensors active; one poller
per sensor duplicates registry and audio enumeration work.
