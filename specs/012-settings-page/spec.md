# Dedicated Settings Page

## Shipped behavior

The main status page remains an at-a-glance view of Home Assistant connection
health and live system state. It links to separate Sensors and Settings pages
without rebuilding the active connection or losing sensor state.

Settings is divided into four sections:

- **General** owns Windows startup and the idle threshold. The idle threshold
  changes only when the `Active` sensor reports idle; it does not lock or sleep
  Windows.
- **Sensors** opens the sensor catalog and provides **Sync sensors now**, which
  refreshes and sends all enabled sensor states immediately.
- **Connection** opens route settings, stops or resumes the current connection,
  and removes the saved server after explicit confirmation. Stopping preserves
  configuration; removal clears the local sign-in and requires another sign-in.
- **About & updates** shows installed and latest-known stable versions and reuses
  the process-wide update state for checks, release installation links, tray
  actions, and the in-app update banner. It also opens the current log file.

Actions report progress and an accessible success or failure result. In demo
mode, controls requiring Home Assistant are hidden while local general settings,
sensor browsing, application updates, and logs remain available.
