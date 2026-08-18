# Tasks: LAN/WLAN Identity, Gateway and DNS Sensors

**Input**: Design documents from `/specs/010-lan-wlan-identity-sensors/`

## Phase 1: Core utilities

- [x] T001 [US1] Add permanent physical MAC lookup via IP Helper P/Invoke in `src/WindowsCompanion.App/Services/NetworkIdentitySensorSource.cs`
- [x] T002 [US1] Add MAC formatting utility in `src/WindowsCompanion.Core/Sensors/MacFormatter.cs`

## Phase 2: User Story 1 - LAN/WLAN MAC addresses

**Independent Test**: Verify permanent MAC is reported regardless of per-network
randomization, and adapters that are up are preferred over merely present ones.

- [x] T003 [US1] Add `lan_mac_address` and `wlan_mac_address` sensor definitions with independent capture scope
- [x] T004 [US1] Add Core unit tests for MAC formatting in `tests/WindowsCompanion.Core.Tests/`

## Phase 3: User Story 2 - Gateway and DNS

**Independent Test**: Verify gateway and DNS values match the active adapter snapshot.

- [x] T005 [US2] Add `gateway_address` and `dns_servers` from the shared adapter snapshot
- [x] T006 [US2] Add formatter tests for DNS joining in `tests/WindowsCompanion.Core.Tests/`

## Phase 4: User Story 3 - Wi-Fi security and randomized MAC

**Independent Test**: Verify security type mapping from native WLAN profile XML and
randomized MAC detection.

- [x] T007 [US3] Parse connected WLAN profile XML for security type and randomized MAC state
- [x] T008 [US3] Add `connectivity_wifi_security` and `connectivity_wifi_random_mac` definitions
- [x] T009 [US3] Add Core unit tests for security mapping in `tests/WindowsCompanion.Core.Tests/`

## Phase 5: Validation

- [x] T010 Validate independent capture-scope gating and privacy model

## Dependencies

- T001 and T002 are required before T003–T004.
- T005–T006 and T007–T009 can run in parallel after Phase 1.
- T010 follows all phases.
