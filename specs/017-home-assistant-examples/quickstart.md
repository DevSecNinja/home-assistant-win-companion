# Quickstart: Validate Home Assistant Examples

## Prerequisites

- A Home Assistant instance supporting `State.last_reported`.
- A registered Windows Companion device with at least one enabled sensor.
- Access to Home Assistant's template editor and configuration.

## Documentation Validation

1. Open the repository README and follow its Home Assistant examples link.
2. Confirm the examples index distinguishes templates from automations.
3. Open the device connectivity example.
4. Confirm it contains all fields required by
   [the example contract](contracts/example-format.md).
5. Confirm the automation category exists and explains future import behavior.

Expected outcome: a new user can locate the example, identify its device name and
timeout placeholders, and understand installation and removal without additional
project documentation.

## Template Validation

1. In Home Assistant, find the exact name of the Windows Companion device.
2. Confirm at least one of its sensors has a `last_reported` that advances when
   the companion performs a periodic sync even if the sensor value is unchanged.
3. Substitute the device name in the example and retain the three-minute default.
4. Validate the template in Home Assistant's template editor.
5. Install or reload the template entity.

Expected outcome: the binary sensor reports connected while companion updates
arrive.

## Stale and Recovery Validation

1. Shut down or disconnect the PC without changing Home Assistant configuration.
2. Wait for the three-minute timeout and the next minute-based template
   evaluation.
3. Confirm the binary sensor reports disconnected.
4. Restart or reconnect Windows Companion and wait for its next successful sensor
   report.
5. Confirm the binary sensor returns to connected.

Expected outcome: Home Assistant derives both transitions from reports it already
receives; no client timestamp or additional heartbeat is created.

## Privacy Validation

Search the example tree for real Home Assistant URLs, webhook IDs, device names,
and personal entity identifiers.

Expected outcome: only clearly documented placeholders and fake examples are
present.
