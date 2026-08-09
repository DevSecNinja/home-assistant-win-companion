# Windows lifecycle signals

What the companion can and cannot know when Windows is about to sleep, sign the
user out, or shut down - and what it does about it.

## What is reported

A single `system_state` sensor (**off by default**, diagnostic category) with the
states `running`, `sleeping`, `signing_out` and `shutting_down`, plus attributes:

| Attribute | Meaning |
| --- | --- |
| `Reason` | The Windows notification behind the state, e.g. `Suspend`, `Sign-out`, `Critical shutdown`. |
| `Critical` | Windows ended the session without the usual grace period. |
| `Since` | When the current state was observed, ISO-8601. |
| `Last Unreported Transition` | A transition Home Assistant never acknowledged, reported once the machine is back. |
| `Last Unreported At` | When that transition was observed. |
| `Last Unreported Reason` | Why it happened. |

The Active and Screen Locked sensors are unchanged. Lock, unlock, fast user
switching and idle stay entirely theirs; `system_state` never reports them, so the
two entities cannot contradict each other. Sleep appears in both - as `Sleeping` in
the Active attributes and as the `sleeping` state here - because they answer
different questions: "is someone using this PC" versus "is this PC going away".

The `hibernating` and `restarting` states exist in the model but Windows never
produces them; see the limits below.

## Signals used

| Signal | Source | Mapped to |
| --- | --- | --- |
| `WM_POWERBROADCAST` / `PBT_APMSUSPEND` | Hidden top-level window | `sleeping` |
| `PBT_APMRESUMESUSPEND`, `PBT_APMRESUMEAUTOMATIC`, `PBT_APMRESUMECRITICAL` | Hidden top-level window | `running` |
| `WM_QUERYENDSESSION` | Hidden top-level window | `signing_out` or `shutting_down` |
| `WM_ENDSESSION` (wParam TRUE) | Hidden top-level window | `signing_out` or `shutting_down` |
| `WM_ENDSESSION` (wParam FALSE) | Hidden top-level window | `running` - another app vetoed the shutdown |
| `SystemEvents.PowerModeChanged` | Managed | `sleeping` / `running` |
| `SystemEvents.SessionEnding` | Managed | `signing_out` / `shutting_down` |
| `SystemEvents.SessionSwitch` (`SessionLogoff` only) | Managed | `signing_out` |

Both paths run at once and are expected to produce duplicates. The tracker in
`HaCompanion.Core` applies the more final transition once, so overlapping and
repeated notifications are idempotent and a late suspend broadcast cannot downgrade
a shutdown that is already under way.

### Why a dedicated hidden window

`WM_QUERYENDSESSION` and `WM_ENDSESSION` are delivered to top-level windows only, so
a message-only (`HWND_MESSAGE`) window would never see a shutdown. The companion's
own WinUI window is not usable either: it lives in the tray and is routinely closed,
which would silently disable the hook. The source therefore owns a hidden top-level
window on its own background thread with its own message pump, created and destroyed
on that thread, and released when the sensor is switched off.

No Windows service is involved. A service runs in session 0 and would lose the
user-session context that the tray, toasts and every other sensor depend on.

Starting and stopping that thread follows a small handshake (`MessagePumpLifetime`,
in Core so it can be tested without Windows). A stop can arrive before the window
exists - at sign-out moments after startup, for instance - and there is then no
window to post `WM_CLOSE` to. The request is therefore recorded first and checked by
the pump on both sides of window creation, and the pump always reports itself ready
even when it never created a window, so a stop can neither be lost nor left waiting.

## Reliability limits

These are properties of Windows, not defects in the companion. The sensor is
therefore **off until you switch it on**, and enabling it asks you to confirm the
same limits first - somebody who expects a guarantee here would eventually file a
missed shutdown as a bug.

- **Sleep and hibernate are indistinguishable before the fact.** Both arrive as
  `PBT_APMSUSPEND`. Nothing in the notification says where memory is going, so both
  report `sleeping`.
- **Shutdown, restart and Windows Update restart are indistinguishable.** The
  `ENDSESSION_*` flags distinguish sign-out from "the machine is going down" and
  nothing more; the reason is reported as `Shutdown or restart` rather than guessing.
- **Modern Standby (S0) may freeze the process instead of notifying it.** On systems
  that never enter S3, a suspend broadcast may not arrive at all, and the process can
  be suspended between reading and sending.
- **Delivery is best effort and often does not happen.** Windows terminates
  applications a few seconds into a shutdown, the network stack can already be down,
  and a webhook acknowledgement over a slow or remote instance rarely fits in what is
  left. Treat a received `shutting_down` as a bonus, not a guarantee.
- **`ENDSESSION_CRITICAL` and forced restarts may skip the messages entirely.**
- **Sudden power loss, battery removal, a kernel crash or `TerminateProcess` produce
  no signal at all** - by definition nothing can be reported or recorded.

Because of all of the above, `system_state` is not suitable as the only trigger for
an automation that matters. Use it to enrich one, or pair it with a timeout on the
Home Assistant side.

## How the companion copes

The local journal is the reliable mechanism; the final push is only an optimisation.

1. A transition is written to `%LOCALAPPDATA%\HaCompanion\lifecycle.json` first,
   marked unacknowledged. It is a separate file from `settings.json` so a write
   interrupted by shutdown cannot damage the configuration or the pointer to stored
   credentials, and every journal operation swallows its own failures.
2. One sensor push is attempted, on a worker thread, with a hard two-second timeout.
   The window procedure returns immediately and always consents to the shutdown:
   the companion never vetoes, never delays, and never shows UI on this path.
3. The record is marked acknowledged only when Home Assistant accepted a batch that
   actually contained that transition. A sync already in flight when the transition
   was observed does not count.
4. On the next start - or on resume - anything still unacknowledged is reported in
   the `Last Unreported *` attributes after the connection is back, and acknowledged
   then. If nothing was recorded, as after a power cut, there is nothing to report
   and the sensor simply reads `running`.

A pending push is cancelled when the machine comes back, so a suspend attempt that
was still waiting cannot land after resume and report a state the machine has left.

## Manual verification

Automated tests cover the mapping, the state machine, the journal and the recovery
path. The following need a real machine, and are part of the pre-release smoke test:

- Sleep and resume; hibernate and resume.
- Shutdown and start; restart; Windows Update restart.
- Sign out and back in; lock and unlock (must not change `system_state`).
- Confirm shutdown is not visibly delayed by the companion.
- Confirm the recovered transition is reported after the next successful connection
  and then stops being reported.
