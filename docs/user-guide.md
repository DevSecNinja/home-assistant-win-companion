# User guide

Windows Companion for Home Assistant is a tray-resident Windows application. It
connects through Home Assistant's built-in `mobile_app` integration, reports only
the sensors you enable, displays Home Assistant notifications as Windows toasts,
and opens the Home Assistant interface in your default browser.

For download, verification, installation, update, startup, and removal steps, see
the [installation guide](installation.md).

## First connection

1. Launch **Windows Companion**.
2. Enter the URL you normally use to reach Home Assistant.
3. Select **Sign in** and complete authentication in your browser.
4. Return to the companion after it registers the PC and connects.
5. Open **Sensors** and enable only the values you want to send.

The refresh token, webhook ID, and cloudhook URL are stored in Windows Credential
Locker. You never need to create or paste a long-lived access token.

## What the companion does

- Lives in the Windows notification area when its window is closed.
- Opens Home Assistant in your default browser instead of embedding a dashboard.
- Receives notifications over Home Assistant's local
  `mobile_app/push_notification_channel` and displays native Windows toasts.
- Reports enabled Windows sensor values on a schedule and when relevant values
  change.
- Shows connection health and keeps a rolling local diagnostic log.
- Checks official releases for newer stable versions without downloading or
  installing anything automatically.

The companion does not run commands, include a media player, host a dashboard, or
run while the Windows user is logged out.

### Choosing a Windows companion

Windows Companion focuses on stock Home Assistant integration, native Windows
behavior, and a small opt-in sensor catalog. It does not require MQTT or a custom
Home Assistant component.

[HASS.Agent](https://github.com/LAB02-Research/HASS.Agent) is the established,
more feature-rich option. Consider it if you need commands, quick actions, a
media player, an embedded Home Assistant view, a much larger sensor catalog, or a
service that can run while logged out.

## Sensors and privacy

Every sensor is individually configurable. The Sensors screen explains its
purpose and resource use and shows a local preview. Disabling a sensor stops its
collection, polling, Windows event hooks, and transmission.

![The Sensors screen with individual descriptions, previews, and toggles.](images/sensors.png)

Privacy-sensitive sensors are disabled by default and are not read for preview
until you enable them. These include network identifiers and values that can add
device fingerprinting detail. Network names configured as trusted networks remain
on the PC and are separate from the optional Wi-Fi sensors.

Available sensor groups include:

- Battery, activity, lock, boot, and lifecycle state.
- Active connection type, IP addresses, LAN MAC address, and optional Wi-Fi
  identifiers.
- Windows version, model, domain or workgroup, and Microsoft Entra ID status.
- Displays, theme, locale, time zone, and system-drive use.
- Notification, presentation, microphone, camera, audio output, and headset state.
- WinGet update count and optional frontmost-application information.

### Automation ideas

The Sensors screen shows a practical automation idea beside each sensor that has
a useful state-driven use case. For example:

- Turn off office lights when the PC becomes inactive, or dim them when the screen
  is locked.
- Use microphone, camera, presentation, or headset state to control an on-air
  light or focus scene without depending on a particular meeting application.
- Activate a work scene when Ethernet connects, the PC joins the office Wi-Fi, or
  a second display appears.
- Mark a room occupied when the PC connects to a specific Wi-Fi access point.
- Change office lighting for dark mode or activate gaming lights for a selected
  frontmost application.
- Send a charging reminder below a chosen battery level, a reboot reminder after
  a long uptime, or a storage warning when free space runs low.
- Enable a travel mode when the Windows time zone changes away from home.
- Send a weekly reminder when WinGet reports available application updates.
- Turn off desk lights when sleep is reported. Lifecycle delivery is best effort,
  so do not use it as the only trigger for critical automations.

These are starting points rather than built-in automations. Enable the relevant
sensor, then create the automation in Home Assistant using the resulting entity.

String sensor states are kept concise for Home Assistant. Additional diagnostics
are placed in attributes. Disabling a previously registered sensor disables its
Home Assistant entity; it does not delete the entity-registry entry.

### Lifecycle signals

The optional `system_state` sensor reports sleep, sign-out, and shutdown without
polling. Windows may terminate applications before a final update can be
delivered, so the companion makes only a short, bounded attempt and never blocks
shutdown. An undelivered transition is recorded locally and reported after the
next successful connection.

See [Windows lifecycle signals](windows-lifecycle-signals.md) for the reliability
limits and recovery behavior.

### WinGet updates

The optional WinGet Updates sensor requires Microsoft's
`Microsoft.WinGet.Client` PowerShell module version 1.29.280 or newer. If it is
missing, the app shows a copyable current-user installation command. The companion
does not install the module itself. Only the update count is sent to Home
Assistant; package names and versions remain in the local preview.

## Connecting from home and away

Most users need only the URL entered during sign-in. If your Home Assistant server
uses a LAN address at home and a public address elsewhere, select **Open settings**,
then **Connection settings**, and enable
**I use different internal and external URLs**.

| Mode | Behavior |
| --- | --- |
| **Automatic** | Uses the internal URL on a trusted network and the external URL elsewhere. |
| **Prefer internal** | Tries the internal URL first and uses the external URL as fallback. |
| **Prefer external** | Tries the external URL first and uses the internal URL as fallback. |
| **Internal only** / **External only** | Never uses the other URL. |

Automatic mode uses the internal URL only on a network you explicitly trust: a
Wi-Fi network by name or, if enabled, any wired connection. Matching a specific
Wi-Fi access point is optional because mesh networks can roam between access
points.

Reading a Wi-Fi network name requires Windows Location permission. Without that
permission, Wi-Fi networks cannot be identified and Automatic mode uses the
external URL.

Before saving two URLs, the companion verifies that both reach the same Home
Assistant device registration. Route changes preserve the refresh token, webhook,
device, and history rather than registering a duplicate device.

The external URL must use HTTPS. Redirects that change host or downgrade HTTPS to
HTTP are rejected, and an address must identify itself as Home Assistant before
credentials are sent. Internal HTTP is allowed with a warning; TLS certificate
validation is never disabled.

## Demo mode

Select **Explore in demo mode** on the sign-in screen to inspect the sensor catalog
without a Home Assistant server. Demo mode does not register a device, save a
session, transmit sensor values, or start background sensor sources. A banner
remains visible until you leave demo mode.

## Updates

Official release builds check GitHub Releases once when the application starts.
When a newer stable release is available, the app shows a toast, tray badge, and
link to the exact release page. Drafts and prereleases are ignored.

The companion never downloads, installs, or restarts itself. Follow the
[installation guide](installation.md#update) to verify and install an update.

## Health and troubleshooting

The status overview reports whether the companion is connected and sending sensor
updates on schedule. Use the app's log action to open the rolling local diagnostic
log.

If the app cannot start, first confirm that Windows App Runtime 2.3 is installed.
Source builds and CI artifacts additionally require the .NET 10 Desktop Runtime.
See the [installation requirements](installation.md#requirements).

If notifications do not arrive, confirm that:

- The companion is connected.
- Windows notifications are enabled for the application.
- The PC remains registered under Home Assistant's **Mobile App** integration.
- The Windows user is signed in and the companion is running.

Windows 11 does not expose its Do Not Disturb switch through a supported API. The
Notification State sensor can report presentation, full-screen, lock-screen, and
legacy quiet-time states, but not that switch.

## Remove a server or uninstall

Use **Remove server...** to clear the saved sign-in and local settings before
uninstalling. Home Assistant's app API cannot delete the Mobile App device entry;
remove it manually under **Settings -> Devices & services -> Mobile App** if
required.

For complete uninstallation and optional local-data cleanup, follow the
[uninstall instructions](installation.md#uninstall).
