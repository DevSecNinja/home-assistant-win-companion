# Branding

The visual identity for **Windows Companion for Home Assistant**. This document is
the reference for anyone changing artwork, adding a surface that shows the product
name, or preparing release and package metadata.

All artwork in this repository is original to this project.

## Name

The product display name is **Windows Companion for Home Assistant**.

The platform is named second, deliberately. Leading with "Home Assistant" implies an
official product, which this is not. Use the full name in window titles, release
names, package metadata and documentation headings. The short form **Windows
Companion** exists only for space-constrained surfaces such as the notification-area
tooltip, which Windows truncates at 127 characters including any status text.

The repository slug (`home-assistant-win-companion`) is unchanged: it is descriptive,
already indexed, and renaming it would break existing links for no benefit.

Both names are defined once in `src/WindowsCompanion.App/Branding.cs`. Use those constants
rather than repeating string literals.

### Technical identity

`WindowsCompanion` is the single token used everywhere the product needs a
machine-readable name. Keep these consistent; a new surface should not invent a
fourth spelling.

| Surface | Value |
| --- | --- |
| Executable | `WindowsCompanion.exe` |
| Assembly | `WindowsCompanion` |
| Projects | `WindowsCompanion.App`, `WindowsCompanion.Core`, `WindowsCompanion.Core.Tests` |
| Namespaces | `WindowsCompanion.Core.*`, `WindowsCompanion_App.*` |
| Data directory | `%LOCALAPPDATA%\WindowsCompanion\` |
| Credential Locker resource | `WindowsCompanion` |
| Startup registry value | `HKCU\...\Run\WindowsCompanion` |
| Release artifacts | `WindowsCompanion-<version>-win-<arch>.zip` |
| Planned WinGet package | `DevSecNinja.WindowsCompanion` |

### The previous identity

Before the rename everything above used `HaCompanion`, and the display name led with
the Home Assistant trademark. Three legacy constants remain in the code purely to
migrate existing installations, and each is the only reference to the old name:

| Constant | File |
| --- | --- |
| `AppDataPaths.LegacyDirectoryName` | `WindowsCompanion.Core/App/AppDataPaths.cs` |
| `WindowsSecretStore.LegacyResource` | `WindowsCompanion.App/Services/WindowsSecretStore.cs` |
| `WindowsStartupRegistration.LegacyValueName` | `WindowsCompanion.App/Services/WindowsStartupRegistration.cs` |

They exist so an upgrade keeps as much as possible: settings and the device id always
migrate, and Credential Locker entries migrate when the OS lets the renamed
executable read them. Because the device id survives, a re-sign-in updates the
existing Home Assistant device instead of creating a duplicate. Do not remove these
constants without a deprecation window, and do not add new references to the old
name.

## Trademark and non-endorsement

This is an independent, community-developed project. The research these rules are
based on, with sources, is in
[`specs/009-branding/trademark-research.md`](../specs/009-branding/trademark-research.md).

> An independent project. Not affiliated with, endorsed by, or sponsored by the Open
> Home Foundation, Nabu Casa, or the Home Assistant project. "Home Assistant" is a
> trademark of the Open Home Foundation. "Windows" is a trademark of the Microsoft
> group of companies.

That notice belongs in the README, the GitHub social preview, package listings, and
any future about dialog. The same text is available as `Branding.TrademarkNotice`.

The mark and the palette must never be used in a way that suggests official status:

- Do not use the Home Assistant house/network logomark, or any recolour, trace or
  derivative of it, anywhere in this project.
- Do not use the Home Assistant cyan `#18BCF2` together with a house silhouette or a
  three-node network graph. That combination is the registered mark.
- Do not use the "Works with Home Assistant" badge, or the "A project from the Open
  Home Foundation" badge. Both are reserved for official or certified participants.
- Do not use a four-pane grid, a tilted pane grid, or Microsoft's four brand colours
  together. That reads as the Windows logo.
- Do not adopt a Segoe Fluent or FluentUI system glyph as the product mark.

## The mark

The mark is an abstracted desktop application window — a title bar above a body,
separated by a knockout gap — with a companion sphere breaking out of its upper-right
corner. The window is the Windows PC; the sphere is the companion process that lives
beside it and reports home.

It nods to the Windows desktop through the single application-window silhouette,
which is explicitly not the four-pane Windows logo: there is one opening, one
horizontal division, and no tilt.

### Geometry

Drawn on a 256-unit grid in `brand/src/mark.svg`.

| Element | Value |
| --- | --- |
| Window extents | x 32–224 |
| Title bar | y 59–99, corner radius 24 |
| Knockout gap | 16 units (y 99–115) |
| Body | y 115–219, bottom corner radius 24 |
| Companion sphere | r 44 at (176, 79) |
| Companion halo | r 60, giving a 16-unit separation |

Sixteen units is the floor for every stroke, gap and halo. Below that the separation
disappears at 16 px. The artwork bounding box is y 35–219, one unit above the
geometric centre, which optically compensates for the bottom-heavy body.

### Clear space

Keep clear space of at least 16 units on the 256 grid — 6.25% of the mark's width —
on all sides. In tile and splash assets the mark is inset considerably further; those
insets are encoded in `brand/build-assets.mjs` and should be changed there, not by
editing exported images.

## Palette

| Role | Hex | Use |
| --- | --- | --- |
| Window | `#2DD4BF` | The window body and title bar; the primary brand colour |
| Companion | `#F59E0B` | The companion sphere; accents that need to draw the eye |
| Ink | `#0F2E2A` | Headings on light surfaces |
| Surface | `#FFFFFF` | Light backgrounds |

The window colour is a spring-shifted cyan, chosen to sit comfortably beside Home
Assistant's `#18BCF2` without being mistaken for it. Amber is its complement and
carries the only warm note in the system.

Contrast: `#0F2E2A` on `#FFFFFF` is roughly 15:1, comfortably past WCAG AA and AAA
for body text. `#2DD4BF` and `#F59E0B` are mid-tone fills, not text colours — do not
set text in either on a white background.

Never encode status through colour alone. The application already pairs its health
colours with text, and any new surface must do the same.

## Variants

| File | Purpose |
| --- | --- |
| `brand/dist/mark.svg` | Full-colour mark |
| `brand/dist/mark-16.svg` | Hand-hinted 16 px mark |
| `brand/dist/mark-mono-dark.svg` | Single colour, for light backgrounds |
| `brand/dist/mark-mono-light.svg` | Single colour, for dark backgrounds |
| `brand/dist/mark-{16..512}.png` | Raster exports |
| `brand/dist/social-preview.png` | GitHub social preview, 1280×640 |

The mark carries its meaning with all colour removed, so the monochrome variants are
straight substitutions rather than redraws. The 16 px variant is the exception: it is
hand-hinted to whole pixels and is intentionally not proportional to the 256 master.

## Regenerating assets

The masters are `brand/src/mark.svg` and `brand/src/mark-16.svg`. **Everything else is
generated.** Never edit an exported PNG, ICO or SVG by hand — the next regeneration
will silently discard the change.

```powershell
.\scripts\build-brand-assets.ps1
```

This rewrites:

- `src/WindowsCompanion.App/Assets/AppIcon.ico` — 16, 20, 24, 32, 40, 48, 64, 128 and
  256 px. Entries up to 64 px are uncompressed 32-bit DIBs, which every Windows icon
  consumer understands; 128 and 256 px are PNG. The 16 px entry comes from the hinted
  master, so the notification-area icon is crisp at 100%, 125%, 150% and 200% scaling.
- the packaging PNGs under `src/WindowsCompanion.App/Assets/`
- the distributable artwork and social preview under `brand/dist/`

To verify committed assets still match the masters without rewriting them:

```powershell
.\scripts\build-brand-assets.ps1 -Check
```

Node.js is required and is pinned in `.mise.toml`; `mise install` provides a matching
version. The social preview renders text with Segoe UI Variable, so regenerate it on
Windows.

After changing a master, check the result at 16 px in monochrome on both a light and a
dark taskbar before committing. That is the size and context the icon actually lives
in, and it is the first thing a change breaks.

## Where the mark is used

| Surface | Source |
| --- | --- |
| Executable icon (Explorer, taskbar, Alt+Tab, SmartScreen) | `ApplicationIcon` in `WindowsCompanion.App.csproj` |
| Window and title-bar icon | `MainWindow.xaml`, `MainWindow.xaml.cs` |
| Notification-area icon | `TaskbarIcon` in `MainWindow.xaml` |
| Future MSIX/WinGet tiles and store logo | `Package.appxmanifest` |
| README and GitHub social preview | `brand/dist/` |

Branding must never replace an accessible text label. Every surface that shows the
mark also names the product in text, so the application stays usable when images fail
to load or are suppressed.
