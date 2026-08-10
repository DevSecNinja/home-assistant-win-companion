# Data Model: Mocked Home Assistant End-to-End Testing

## FakeHaScenario

Represents all mutable state owned by one automated scenario.

| Field | Type | Rules |
|------|------|-------|
| ScenarioId | String | Required, unique, safe for file names and diagnostics |
| BaseUrl | URI | Assigned after startup; loopback HTTP only |
| InstanceDeviceId | String | Synthetic, stable for the scenario |
| AccessToken | String | Synthetic, never emitted in logs |
| RefreshToken | String | Synthetic, never emitted in logs |
| WebhookId | String | Synthetic, never emitted in logs |
| State | FakeHaState | Required, one instance per scenario |
| Faults | FakeHaFaults | Required, resettable |
| Interactions | InteractionLog | Append-only during the scenario |

**Lifecycle**: Created → Starting → Running → Stopping → Disposed.

## FakeHaState

Represents server-observed registration and connection state.

| Field | Type | Rules |
|------|------|-------|
| Registrations | Collection | Device ID is unique; duplicate registration is observable |
| RegisteredSensors | Map | Keyed by stable sensor unique ID |
| SensorStates | Map | Only registered/enabled sensors may be updated |
| WebSocketSessions | Collection | Tracks authenticated and subscribed sessions |
| ConfirmedNotifications | Collection | Confirmation ID is unique |
| RevokedRefreshTokens | Collection | Values are redacted in diagnostics |

**Registration transitions**:

Unregistered → Registered → Registration Updated. A restart with persisted state
must remain Registered rather than creating another registration.

**WebSocket transitions**:

Connected → Auth Required → Authenticated → Push Subscribed → Disconnected.
Authentication rejection transitions directly to Disconnected.

## FakeHaFaults

Typed, scenario-controlled server behavior.

| Fault | Effect |
|------|--------|
| RejectAuthorizationCode | Token exchange returns an OAuth rejection |
| RejectRefreshToken | Refresh returns an OAuth rejection |
| ApiUnavailable | REST calls fail or disconnect deterministically |
| MobileAppUnavailable | Registration reports that the integration is unavailable |
| RejectSensor | Selected sensor update returns an HA body-level rejection |
| UnknownWebhook | Webhook identity returns the HA unknown-registration behavior |
| ClosePushChannel | Active WebSocket closes at the selected protocol step |
| Delay | Selected interaction waits on a test-owned release signal |

Faults are inactive by default. Activation and release are explicit; disposal
releases all waits.

## FakeHaInteraction

Sanitized record of one observable protocol event.

| Field | Type | Rules |
|------|------|-------|
| Sequence | Integer | Monotonically increasing per scenario |
| Timestamp | Date/time | UTC |
| Kind | Enum | Authorization, token, API, registration, webhook, WebSocket, notification |
| Method | String | HTTP method or WebSocket direction |
| PathOrMessageType | String | No query secrets or webhook values |
| CorrelationId | String | Scenario-generated when applicable |
| Payload | Structured value | Redacted before storage |
| Outcome | String | Success, rejected, disconnected, cancelled |

Interaction waiters match typed predicates and complete through handshakes rather
than polling.

## CompanionTestProfile

Isolated state used by one controller or UI process.

| Field | Type | Rules |
|------|------|-------|
| ProfileId | String | Unique per test |
| SettingsDirectory | Path | New temporary directory |
| CredentialResource | String | Unique Windows Credential Locker resource |
| InstanceIdentity | String | Unique app mutex/shutdown identity |
| ServerUrl | URI | Must be loopback in test composition |
| AutoAuthorize | Boolean | Enabled only for debug test builds |

**Lifecycle**: Allocated → In Use → Restarted zero or more times → Cleaned.
Cleanup removes files, credential entries, and owned processes.

## FailureEvidence

Artifacts retained when a scenario fails.

| Field | Type | Rules |
|------|------|-------|
| ScenarioId | String | Matches the test case |
| TestResult | TRX | Always generated |
| InteractionLog | JSON | Sanitized |
| AppLog | Text | Sanitized; copied from isolated profile |
| Accessibility tree | Structured text | UI tests only and sanitized before retention |

No artifact may contain access tokens, refresh tokens, webhook IDs, personal
endpoints, Wi-Fi identifiers, or sensitive sensor values.

## Relationships

- One `FakeHaScenario` owns one `FakeHaState`, one `FakeHaFaults`, and one
  `InteractionLog`.
- One automated test owns one `FakeHaScenario` and one `CompanionTestProfile`.
- A UI test owns one companion process at a time.
- `FailureEvidence` references one scenario and is produced only by its fixture.
