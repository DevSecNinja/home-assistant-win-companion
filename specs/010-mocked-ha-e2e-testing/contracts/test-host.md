# Contract: Application Test Host

## Production composition

The parameterless application/controller path remains the shipped default and
uses the normal settings directory, standard Credential Locker resource, real
default-browser launcher, Windows sensors, network monitoring, toasts, logging,
startup registration, and global single-instance identity.

## Injectable controller composition

`AppController` accepts an internal dependency bundle for tests. The bundle
provides:

- HTTP client and WebSocket factory
- OAuth URI launcher
- settings and secret stores
- network and system-status providers
- sensor-source factory
- notification sink
- logger factory

Production creation supplies the existing concrete services. Tests must not
replace the Home Assistant REST client, OAuth client, WebSocket client,
registration workflow, connection manager, sensor synchronization service, or
lifecycle coordinator.

Owned dependencies declare whether the controller disposes them. Repeated
controller construction over the same profile proves persisted-session behavior.

## Debug-only executable launch

The UI-test build accepts one structured test-profile argument containing:

- temporary settings directory
- unique Credential Locker resource suffix
- unique single-instance identity
- loopback fake server URL
- automatic authorization flag

Validation rules:

- The contract is compiled only into Debug test builds.
- The server and authorization targets must be loopback.
- Paths must be absolute and scenario-owned.
- Missing, malformed, or unsafe options fail visibly before changing user state.
- Release builds reject or ignore no test options because the code is absent.

## Cleanup

The fixture requests graceful shutdown first, then terminates only the exact
process it launched if the timeout expires. It removes the scenario settings
directory and uniquely scoped credential entries. It never kills processes by
name or clears the user's normal app data.
