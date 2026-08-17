# Data Model: Home Assistant Examples

## Example

A reusable Home Assistant configuration published by the project.

| Field | Description | Validation |
| --- | --- | --- |
| Name | Human-readable purpose | Unique within its category |
| Slug | Stable example directory name | Lowercase, hyphen-separated |
| Category | Home Assistant artifact type | One of the documented categories |
| Prerequisites | Required companion and server capabilities | Must include minimum supported behavior |
| Installation | Steps that activate the example | Must be complete without external assumptions |
| Customization | Values the user must or may change | Every placeholder must be described |
| Expected behavior | Observable normal and failure behavior | Must include timing where applicable |
| Removal | Steps that undo installation | Must not leave unexplained artifacts |

## Example Category

Determines storage and installation semantics.

| Category | Purpose | Initial contents |
| --- | --- | --- |
| Templates | Entities derived from Home Assistant state | Device connectivity |
| Automations | Future actionable workflows and importable automation artifacts | Category guidance |

## Companion Device

A Windows Companion device selected by exact Home Assistant name as evidence of
recent communication.

| Field | Description | Validation |
| --- | --- | --- |
| Device name | Home Assistant device name | Must be unique and resolve by exact name |
| Sensor entities | Enabled sensors associated with the device | At least one must have reported |
| Latest report | Newest server-maintained report time across sensor entities | Must be available on a state object |
| Sync cadence | Expected interval between reports | Timeout must exceed ordinary cadence |

## Connectivity State

| State | Condition | Transition |
| --- | --- | --- |
| Connected | The device has a sensor report within the timeout | Enter after any fresh sensor report |
| Disconnected | The device is unresolved, has no reported sensors, or its newest report exceeds the timeout | Enter after timeout plus template evaluation |

The state is derived and is not persisted or transmitted by the Windows client.
