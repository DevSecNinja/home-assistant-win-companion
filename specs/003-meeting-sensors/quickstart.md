# Quickstart: Meeting Context Sensors

## Build and test

```powershell
.\scripts\run.ps1
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj
```

## Notification state

1. Open **Sensors…** and confirm **Notification State** is enabled and shows a
   preview.
2. Start a full-screen application or enable Windows presentation mode.
3. Within 15 seconds, confirm the preview and Home Assistant entity change.
4. Return to normal desktop use and confirm the state returns to
   `Accepts Notifications`.

## Microphone and camera

1. Enable **Microphone In Use** and **Camera In Use**.
2. Open a packaged application and a traditional desktop application that use each
   device.
3. Confirm each binary entity turns on while the device is active and off after use
   ends.
4. Disable both sensors and confirm their Home Assistant entities become disabled.

## Audio output and headset

1. Enable **Audio Output** and **Headset Connected**.
2. Change the Windows default output and confirm the friendly name updates.
3. Connect and disconnect a headset and confirm the binary entity follows.
4. Verify a non-headset speaker remains available as Audio Output without making
   Headset Connected turn on.

## Polling lifecycle

Disable all sensors belonging to one source, wait more than 10 seconds, and verify
no source-driven update occurs. Re-enable one sensor and verify changes resume.
For the shared microphone/camera source, verify activity changes are sampled once
per second without producing updates while the sampled state is unchanged.
