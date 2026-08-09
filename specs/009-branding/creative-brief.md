# Creative brief — Windows Companion for Home Assistant

Working document for issue #41. Not final documentation.

## Product

A lightweight, unpackaged WinUI 3 tray utility for Windows that connects a Windows
PC to a self-hosted Home Assistant instance. It publishes PC sensors (battery,
network, session state, active app, lifecycle events) and receives notifications.
It lives in the notification area and is normally invisible.

It is an **independent third-party project**. It is not made, endorsed, or
supported by Home Assistant, the Open Home Foundation, Nabu Casa, or Microsoft.

## Name decision

The product display name is **Windows Companion for Home Assistant**, paired with
a non-endorsement notice. This descriptive form positions Home Assistant as the
platform rather than as this product's brand, which is the lower-risk pattern
under nominative fair use. The GitHub repository slug
(`home-assistant-win-companion`) is unchanged.

Because the name is descriptive rather than coined, the *mark* must carry the
distinctiveness the name does not: it must be visibly its own thing and must not
lean on Home Assistant's visual identity to be understood.

## Personality

Lightweight, native, quiet, trustworthy, technically reliable.

Not: dashboard-heavy, playful, cartoonish, "smart home gadget", neon,
gradient-glow SaaS, AI-assistant orb.

## Primary contexts, in priority order

1. **16 px monochrome notification-area icon** at 100/125/150/200% scaling. This is
   the icon's real life. If it fails here it fails.
2. Windows taskbar and window/title-bar icon, light and dark themes.
3. GitHub repository, README, release page, social preview.
4. Future WinGet search result listing.
5. SmartScreen / installer trust context — must read as sober software, not malware-y.

## Hard constraints

- Original artwork only. No use, recolour, trace, or near-copy of the Home
  Assistant house/network mark, the Home Assistant Companion mobile icon, the
  HASS.Agent mark, the Windows logo, or any Microsoft Fluent system glyph.
- Do not depend on Home Assistant blue (`#03A9F4`) as the brand colour. An
  incidental accent is not a brand.
- Must survive being reduced to a single flat colour with no gradients, no
  strokes below ~1/16 of the canvas, and no detail that vanishes at 16 px.
- Must work on both light and dark taskbars, in grayscale, and in Windows
  high-contrast mode.
- Must fit Windows 11 Fluent visual language: geometric, restrained, generous
  corner radii, optical rather than mathematical centring.
- Status must never be communicated by colour alone.

## Concept territory (suggestions, not requirements)

Ideas that communicate *Windows desktop ↔ home, connection, presence, quiet
background service* without borrowing anyone's mark. For example: a link or
handshake between two forms; a desktop/monitor silhouette abstracted; a signal or
pulse; a doorway/threshold; a companion satellite next to a larger body; a plug
or bridge. Do not feel bound by this list — an unexpected but legible idea is
better than a safe generic one.

## Deliverables per concept

See the design-battle instructions issued to each concept author.
