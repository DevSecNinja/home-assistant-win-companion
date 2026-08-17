# Tasks: Sensor Search Filter

**Feature**: specs/016-sensor-search-filter
**Generated**: 2026-08-17
**Total Tasks**: 5

## Phase 1: Setup

_(No setup tasks — feature modifies existing files only)_

## Phase 2: Foundational

_(No foundational tasks — no shared prerequisites)_

## Phase 3: User Story 1 — Filter sensors by name (P1)

**Goal**: Users can type into a search box to filter the sensor list by name.

**Independent Test**: Open sensors page, type a partial name, verify list filters; clear search, verify all sensors return.

- [x] T001 [US1] Add AutoSuggestBox to the sensors panel header in `src/WindowsCompanion.App/MainWindow.xaml` between the header StackPanel and the ScrollViewer (inside `SensorsPanel` Grid row 0 area). Give it `x:Name="SensorSearchBox"`, `AutomationProperties.AutomationId="Sensors.SearchBox"`, placeholder text "Filter sensors…", `QueryIcon` as Find, and wire `TextChanged` to `OnSensorFilterChanged`.
- [x] T002 [US1] Add a "no results" TextBlock (`x:Name="SensorSearchEmpty"`, `Visibility="Collapsed"`) inside the `SensorList` parent StackPanel showing "No sensors match your search" when filter yields zero visible items.
- [x] T003 [US1] Implement `OnSensorFilterChanged` in `src/WindowsCompanion.App/MainWindow.Sensors.cs`: iterate `SensorList.Children`, compare each card's sensor name (stored as `Tag` or extracted from the first `TextBlock` heading) against the search text using `Contains` with `StringComparison.OrdinalIgnoreCase`, set `Visibility` to `Visible` or `Collapsed`. Show/hide `SensorSearchEmpty` based on whether any cards are visible.
- [x] T004 [US1] Store the sensor name on each card's `Tag` property (or a wrapper) during `BuildSensorListAsync` so the filter can access it without walking the visual tree.
- [x] T005 [US1] Clear `SensorSearchBox.Text` in `OnCloseSensors` (when leaving the page) to reset filter state on re-entry. Re-apply active filter after `BuildSensorListAsync` rebuilds the list.

## Phase 4: User Story 2 — Instant feedback while typing (P2)

_(Covered by US1 implementation — `TextChanged` fires on every keystroke. No additional tasks.)_

## Phase 5: Polish & Cross-Cutting

_(No additional polish tasks — AutoSuggestBox provides clear button, keyboard support, and Fluent styling out of the box.)_

## Dependencies

```text
T004 → T003 (filter logic needs name stored on card)
T001 → T003 (XAML element must exist before code-behind references it)
T002 → T003 (empty-state element must exist before filter toggles it)
T005 depends on T001 (needs SensorSearchBox to exist)
```

## Parallel Opportunities

T001 and T004 can be done in parallel (XAML vs code-behind changes in different files — but T004 modifies the same code-behind file as T003, so best done sequentially).

## Implementation Strategy

**MVP**: Complete Phase 3 (all 5 tasks) — delivers the full feature since US2 is inherently satisfied by the `TextChanged` event approach.
