# Pre-release smoke test

Run this checklist on a clean Windows user profile against a non-production Home
Assistant instance.

## Install and first launch

- [ ] Install the required Windows App Runtime.
- [ ] Build or unpack the exact release candidate.
- [ ] Launch the executable and confirm the Connect view appears without an apphost
      error dialog.

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

## Logs and cleanup

- [ ] Open the log from the UI and inspect it for secrets or sensitive sensor
      values, including IP, IPv6 and MAC addresses.
- [ ] Delete the test Mobile App device and any test notifications from Home
      Assistant.
