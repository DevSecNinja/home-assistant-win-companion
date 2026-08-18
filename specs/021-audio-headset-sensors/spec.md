# Feature Specification: Audio/Headset Sensors

**Status**: Shipped

Add opt-in `audio_output` and `headset_connected` sensors that report the current
default audio output device and whether a headset/headphones/earbuds endpoint is
present.

## Requirements

- Both sensors default off and are labelled privacy-sensitive.
- `audio_output` reports the friendly name of the default Windows audio render
  endpoint. Reports `Not Connected` when no audio device exists.
- `headset_connected` is a binary sensor that is on while any audio render or
  capture endpoint matches headset keywords (headset, headphone, earbud, AirPod,
  Jabra, Poly, Plantronics).
- Headset classification is deterministic and lives in Core (`HeadsetClassifier`).
- Polling every 10 seconds with push only on actual change; disabled means zero
  device enumeration.
- COM and unauthorized exceptions produce safe empty readings.

## Privacy

- Device names are not logged.
- Local preview enumerates devices only while requested.
