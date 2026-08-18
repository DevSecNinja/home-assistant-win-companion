# Sensors Page Preview Contract

1. Opening the Sensors page performs its existing initial preview read and then begins automatic refresh.
2. Automatic refresh updates only enabled sources that explicitly provide an existing cached snapshot; it does not call the general sensor read contract, collect in demo mode, probe disabled sources, recreate rows, alter toggles, change search text, or initiate Home Assistant synchronization.
3. Refresh runs no more frequently than every two seconds and never overlaps a previous page-driven preview read.
4. Navigating away, hiding to tray, minimizing, suspending, or shutting down stops scheduling and cancels the active page preview.
5. Returning to an actively presented Sensors page requests a fresh preview immediately.
6. Disabled sensitive sensors retain their disabled-preview message without querying their source.
7. A missing or failed source preview does not erase a previously displayed value or stop future refreshes.
