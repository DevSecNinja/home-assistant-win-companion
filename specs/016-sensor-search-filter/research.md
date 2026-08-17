# Research: Sensor Search Filter

## Decision: Use AutoSuggestBox for search input

**Rationale**: WinUI 3's `AutoSuggestBox` provides a native search-style text input with a built-in clear button (X), search icon, and proper keyboard handling. It follows Fluent Design out of the box and matches user expectations for a filter/search field on Windows.

**Alternatives considered**:
- Plain `TextBox`: Would require manually adding a clear button and search icon. More work for the same result.
- `TextBox` with debounce timer: Over-engineering — the sensor list is small enough (~50 items) that filtering on every keystroke is fine without debouncing.

## Decision: Filter by toggling Visibility on existing children

**Rationale**: The sensor list is built imperatively (`SensorList.Children`). Filtering by setting `Visibility = Collapsed` on non-matching cards is the simplest approach — no need to rebuild the list or maintain a separate data source. The list is small enough that iterating all children on each keystroke is negligible.

**Alternatives considered**:
- Rebuilding the list on each filter change: Expensive (async preview fetching), disruptive (loses toggle state), unnecessary.
- Maintaining a separate `ObservableCollection` with data binding: Would require a significant refactor of the imperative list-building pattern. Disproportionate to the feature.

## Decision: Match against sensor name only

**Rationale**: The issue asks to "filter sensors by name". Matching only the visible sensor name keeps behavior predictable and the implementation simple. Description matching could surface unexpected results.

**Alternatives considered**:
- Matching name + description: Could be added later but risks confusing matches. Not requested.
- Matching sensor unique ID: Internal detail, not useful to users.
