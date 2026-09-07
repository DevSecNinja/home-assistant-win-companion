# Research: Monitor Identification Sensor

## Decision 1: Extend the existing display source

**Decision**: Add `monitor_identity` to `DisplaySensorSource` and extend its
capture policy so monitor identity is gathered only when this sensor is enabled
or explicitly previewed.

**Rationale**: The source already owns active-display enumeration,
`DisplaySettingsChanged`, count-only privacy isolation, deterministic display
formatting, and start/stop lifecycle. A separate source would duplicate the same
Windows query and event hook and could publish inconsistent snapshots.

**Alternatives considered**:

- A separate monitor sensor source: rejected because it duplicates topology
  collection and lifecycle.
- Dynamic per-monitor sensor entities: rejected because monitor entities would
  churn when docking and are explicitly out of scope.

## Decision 2: Use CCD target device names

**Decision**: Query active paths with `QueryDisplayConfig` and request
`DISPLAYCONFIG_TARGET_DEVICE_NAME` for each active target through
`DisplayConfigGetDeviceInfo`.

**Rationale**: Microsoft documents this structure as the supported source for the
monitor-friendly device name, EDID manufacturer ID, EDID product code, output
technology, and monitor device path. It is available on all supported Windows
versions and is already adjacent to the source's existing CCD query.

**Alternatives considered**:

- WMI `WmiMonitorID`: rejected because it adds a slower, failure-prone subsystem
  when the supported CCD query already has the required active-target data.
- Reading raw EDID from the registry: rejected because it expands permissions and
  parsing scope and may enumerate inactive devices.
- SetupAPI enrichment: deferred because the CCD friendly name is sufficient for
  the requested brand/model display and avoids another device-enumeration pass.

## Decision 3: Treat the Windows friendly name as the model label

**Decision**: Expose the normalized `monitorFriendlyDeviceName` as `model`, the
decoded three-letter EISA/PNP value as `manufacturer`, and the EDID product value
as a four-digit uppercase hexadecimal `product_code`. For one monitor, the
friendly model label is the state. If it is absent, use manufacturer plus product
code. If an active, available physical target has no readable identity fields,
retain it as `Unknown monitor`; disconnected/forced targets are excluded by the
active-path availability signal.

**Rationale**: Windows provides a user-facing monitor name but not a guaranteed
separate full marketing brand. The standardized manufacturer code is reliable
when EDID IDs are valid, while the friendly name commonly contains the familiar
brand and model. This avoids brittle token parsing or an incomplete vendor-name
database.

**Alternatives considered**:

- Split the friendly name at the first space: rejected because multi-word brands
  and manufacturer-code/name mismatches make it unreliable.
- Bundle a PNP vendor database: rejected because it adds a large, independently
  maintained dataset for little additional automation value.

## Decision 4: Keep unique device paths internal

**Decision**: Use the monitor device path only as an in-memory deduplication key
when present, with adapter/target identity as a fallback. Do not send it to Home
Assistant, previews, or logs.

**Rationale**: Distinct identical monitors must remain separate, but the user
asked for brand and type rather than unique hardware inventory. Keeping the key
internal minimizes fingerprinting and corrects the earlier design boundary only
as far as this feature requires.

**Alternatives considered**:

- Expose the device path: rejected as unnecessary sensitive detail.
- Deduplicate by brand/model: rejected because two identical physical monitors
  would incorrectly collapse.

## Decision 5: Distinguish unavailable from headless

**Decision**: Model capture as either successful with a possibly empty monitor
list or unavailable. CCD failures such as access denied, unsupported driver, or
repeated insufficient-buffer races, or an inability to correlate an active target
to its desktop source report `Unavailable`; a successful active-path query with
no available physical targets reports `No monitors`.

**Rationale**: An empty successful result is meaningful for a headless desktop.
Conflating API failure with that state would make automations unreliable.

**Alternatives considered**:

- Convert every failure to an empty list: rejected because it creates false
  headless transitions.
- Retain the last successful reading silently: rejected because it presents stale
  hardware as current.

## Decision 6: Retry topology buffer races

**Decision**: Retry the `GetDisplayConfigBufferSizes` / `QueryDisplayConfig`
sequence a small bounded number of times when Windows reports
`ERROR_INSUFFICIENT_BUFFER`.

**Rationale**: Microsoft documents that topology may change between the two calls.
A bounded retry handles normal dock-settling races without hanging or broad error
suppression.

**Alternatives considered**:

- No retry: rejected because normal topology changes would surface avoidable
  unavailable readings.
- Unbounded retry: rejected because a rapidly changing or faulty display stack
  could block sensor collection.

## Sources

- Microsoft Learn, `DISPLAYCONFIG_TARGET_DEVICE_NAME`:
  <https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name>
- Microsoft Learn, `QueryDisplayConfig`:
  <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig>
- Microsoft Learn, `DisplayConfigGetDeviceInfo`:
  <https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo>
- Microsoft PowerToys, CCD target structure interop:
  <https://github.com/microsoft/PowerToys/blob/50baadc0f36c1cea2db939653711bee4b6f79201/src/modules/powerdisplay/PowerDisplay.Lib/Drivers/NativeStructures/DisplayConfigTargetDeviceName.cs>
