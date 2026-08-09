# Pre-release smoke test

Run this checklist on a clean Windows user profile against a non-production Home
Assistant instance.

## Install and first launch

- [ ] Install the required Windows App Runtime.
- [ ] Install the x64 or ARM64 release candidate without administrator elevation.
- [ ] Confirm the Start Menu shortcut and Apps & Features entry use the
      `WindowsCompanion` product name.
- [ ] Launch from the Start Menu and confirm the Connect view appears without an
      apphost error dialog.
- [ ] With the companion running, start the same setup again and confirm it clearly
      asks for the app to be closed instead of failing on locked files.
- [ ] Run a newer setup over an existing install and confirm settings, Credential
      Locker secrets and the Home Assistant device registration are preserved.
- [ ] Verify `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` install and uninstall for
      future WinGet use.
- [ ] Uninstall and confirm application files, Start Menu shortcut, Apps & Features
      entry and Start with Windows value are removed, while settings/logs remain.
- [ ] Extract the portable ZIP and confirm all files are contained in one versioned
      top-level folder.

## Sign in and registration

- [ ] Enter the Home Assistant URL and complete browser sign-in.
- [ ] Confirm one Mobile App device is created with the expected PC name.
- [ ] Restart the companion and confirm the session resumes without another login.
- [ ] Confirm the refresh token and webhook ID do not appear in `settings.json` or
      the log.

## Sensors

- [ ] Open Sensors and confirm every sensor shows a local preview, and that IP
      address, IPv6 address, MAC address and the Wi-Fi identifiers instead show an
      opt-in placeholder while they are switched off.
- [ ] Enable IPv6 address and MAC address and confirm each reports the active
      adapter's values, that enabling one does not reveal the other, and that both
      report `Not Connected` when the network is unplugged.
- [ ] With a VPN or Hyper-V/WSL adapter up, confirm IPv4, IPv6 and MAC still
      describe the physical Ethernet/Wi-Fi adapter.
- [ ] Move between Ethernet and Wi-Fi and confirm the readings follow the active
      adapter without a restart.
- [ ] Toggle one sensor off and confirm its Home Assistant entity becomes disabled.
- [ ] Toggle it on and confirm it reports again.
- [ ] Select Update now and confirm Last update advances.

## Notifications

- [ ] Send a notification through `notify.mobile_app_<device>` and confirm a native
      Windows toast appears.
- [ ] Click the toast and confirm the companion window is restored.
- [ ] Hide the app in the tray and confirm a second notification still arrives.

## Connection lifecycle

- [ ] Disconnect and confirm reporting/notifications pause without losing the saved
      server.
- [ ] Reconnect without signing in again.
- [ ] Restart Home Assistant or interrupt the network and confirm the app reconnects.
- [ ] Remove server, confirm the destructive prompt, and verify local credentials
      and configuration are deleted.

## System lifecycle

- [ ] Enable **System State** in the sensor list: the best-effort dialog appears,
      **Cancel** leaves the toggle off and nothing is saved, and **Enable anyway**
      turns it on. The `best effort` badge shows next to the sensor.
- [ ] Sleep and resume; confirm `system_state` returns to `running` and that any
      undelivered `sleeping` transition appears in the `Last Unreported *`
      attributes and then stops being reported.
- [ ] Hibernate and resume (reported as `sleeping`).
- [ ] Sign out and back in; restart; shut down and start; a Windows Update restart
      where feasible.
- [ ] Confirm shutdown and sleep are never visibly delayed by the companion and no
      dialog appears.
- [ ] Lock and unlock, and switch users, and confirm `system_state` does not change.
- [ ] Confirm no duplicate or flapping history entries from overlapping signals.

## Logs and cleanup

- [ ] Open the log from the UI and inspect it for secrets or sensitive sensor
      values, including IP, IPv6 and MAC addresses.
- [ ] Delete the test Mobile App device and any test notifications from Home
      Assistant.
