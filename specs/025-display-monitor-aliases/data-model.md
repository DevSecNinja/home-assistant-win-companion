# Data Model: Display and Monitor Search Aliases

## Sensor Definition

Static catalog metadata describing one sensor.

| Field | Meaning | Rules |
|-------|---------|-------|
| Unique ID | Registration and preference identity | Uses `display_identity` for the current contract |
| Name | Current user-visible sensor name | Uses canonical catalog terminology |
| Description | User-facing explanation | May contain natural terminology but is not the alias contract |
| Search aliases | Optional additional local search terms | Static, case-insensitive, not transmitted |

### Validation Rules

- Definitions without aliases behave exactly as before.
- Aliases do not need to repeat the visible name.
- Duplicate or differently cased aliases are harmless because search matching is
  case-insensitive.
- Empty aliases contribute no search text.

## Sensor Search Index

The local searchable text associated with one rendered sensor card.

It consists of the visible name, description, configured aliases, and existing
UI-only badges. It is used only to decide whether the local card is visible.

## Identity and Lifecycle

- `display_identity` is the unique ID for the current sensor.
- An old `monitor_identity` registration is absent from the catalog and is
  retired by the existing removed-sensor synchronization behavior.
- Existing `monitor_identity` enablement preferences are not part of the new
  contract; `display_identity` uses its disabled-by-default definition.
- Search aliases have no runtime lifecycle and do not start sensor sources.
