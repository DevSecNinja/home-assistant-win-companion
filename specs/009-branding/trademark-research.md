# Trademark, Branding & Logo Usage Research Report
## For: `DevSecNinja/home-assistant-win-companion`
### Research date: 2026-08-09

> Background research for [issue #41](https://github.com/DevSecNinja/home-assistant-win-companion/issues/41).
> This report is what the naming and artwork decisions in
> [`docs/branding.md`](../../docs/branding.md) are based on. It records findings and
> citations as of the research date; it is not legal advice, and OHF has published no
> formal third-party naming policy, so re-check the cited sources before relying on
> them. Note that the product display name has since been changed to
> **Windows Companion for Home Assistant**, which is the lower-risk form this report
> recommends.

---

## Executive Summary

No single, standalone public "Home Assistant Trademark Policy" page exists at the URLs one would expect (`home-assistant.io/trademark/`, `openhomefoundation.org/trademark/` — both return 404). Policy is instead scattered across several sources: the **OpenHomeFoundation/brand-assets** GitHub repo README, the **home-assistant/brands** GitHub repo README, a copyright notice at `brands.openhomefoundation.io` (not publicly accessible via DNS at time of research), the **Works with Home Assistant** partner program site, and general trademark-law norms. The OHF brand-assets README is the **most authoritative public statement found**. Microsoft's trademark policy is well-documented. HASS.Agent (the closest adjacent project) carries **no explicit non-endorsement disclaimer** in its current README — it self-identifies by description only.

---

## 1. Official Open Home Foundation / Home Assistant Trademark Policy

### 1.1 Trademark ownership

**"Home Assistant" is a registered trademark of the Open Home Foundation.** It has been filed internationally (WIPO application no. 1874512) and in Europe (EUIPO no. 018721682). As of 2024, the Open Home Foundation (a Swiss non-profit) owns and governs the mark; previously it was held by Nabu Casa.

Sources:
- WIPO filing: https://www.trademarkelite.com/wipo/trademark/trademark-detail/1874512/Home-Assistant
- EUIPO filing: https://www.trademarkelite.com/europe/trademark/trademark-detail/018721682/Home-Assistant
- OHF governance announcement: https://www.openhomefoundation.org/blog/announcing-the-open-home-foundation/

### 1.2 The most authoritative public statement: OpenHomeFoundation/brand-assets README

**Verbatim:**
> "This logo is trademarked and the property of the Open Home Foundation. This means it is **not available for commercial use without express written permission from the foundation**. We regard commercial use as anything designed to market or promote a product, software or service that is for sale. Please contact partner@openhomefoundation.org for further information."
> — `OpenHomeFoundation/brand-assets` README, SHA `1a29bc4`, https://github.com/OpenHomeFoundation/brand-assets/blob/163c9422939a014f5866c01c176fbb332e2aba08/README.md

This statement is specifically about **logos and graphic marks**. It covers the Open Home Foundation's own marks (OHF, Home Assistant, ESPHome, Music Assistant). It says logos cannot be used in commercial contexts without permission; it does not directly address the *wordmark* (the name "Home Assistant" as text).

A brand guidelines microsite is referenced (`brands.openhomefoundation.io`) but that domain was **not resolving** at research time. No detailed do's/don'ts for third parties are yet publicly published there.

### 1.3 home-assistant/brands repo (integration image library)

This repo governs icons for HA integrations, not third-party app naming. However, it contains a key rule verbatim:

> "**Custom integrations must not use Home Assistant branded images**, as this might confuse the end-user into thinking that the integration is an internal/official integration."
> — `home-assistant/brands` README, https://github.com/home-assistant/brands/blob/e1d1c9655f81f1805e8c8379aa815873e352ec81/README.md

And its own disclaimer:

> "All product names, trademarks and registered trademarks in the images in this repository, are property of their respective owners. All images in this repository are used by the Home Assistant project for identification purposes only. The use of these names, trademarks and brands appearing in these image files, do not imply endorsement."
> — ibid.

### 1.4 Works with Home Assistant (badge program)

The "Works with Home Assistant" badge is **exclusively for certified hardware partners** who sign an agreement and pay an annual fee (500 CHF). Only certified partners may display this badge. **This program is not relevant to software companions.**

Source: https://works-with.home-assistant.io/

### 1.5 No explicit "nominative/descriptive use" policy found

⚠️ **Gap:** The OHF has not published a document equivalent to Mozilla's "Community Edition" policy, WordPress's "Nominative Use" rules, or Linux Foundation guidelines explicitly permitting third-party projects to use the name "Home Assistant" in descriptive contexts. The closest public statement is general trademark law + the brand-assets copyright notice above. No "you may use 'Home Assistant' in your name if…" clause exists in writing.

---

## 2. Using "Home Assistant" in a Third-Party Project Name

### 2.1 What trademark law says (nominative/descriptive fair use)

In both US and EU trademark law, *nominative fair use* permits a third party to use a trademark **solely to describe the product the mark refers to**, provided:
1. The product/service cannot be readily identified without using the mark.
2. Only so much of the mark is used as necessary.
3. Nothing is done to suggest sponsorship, affiliation, or endorsement.

This would generally allow a phrase like **"a Windows companion for Home Assistant"** in a description. It is much riskier to incorporate the mark directly into a **product's primary name**, especially at the front (e.g., "Home Assistant Windows Companion" could be read as an official product).

### 2.2 Community precedent and general guidance

The web search synthesis (corroborating trademark law norms applied to the HA community) found the following informal guidance consistently repeated:
- **Avoid putting "Home Assistant" as the first word** in a product name — it implies the product is official or from the HA team.
- **"[YourBrand] for Home Assistant"** is the generally accepted pattern — it clearly positions HA as the platform, not as the product's own brand.
- **A non-endorsement disclaimer is expected** by community norms, even if not formally required in writing by OHF.

### 2.3 Current repo name observation

The current GitHub repo name is `home-assistant-win-companion` (all lowercase, hyphenated — fine as a technical repo slug). The README heading **"Home Assistant Windows Companion"** starts with the trademark — this is the riskiest form.

**Safer alternatives:**
- "Windows Companion for Home Assistant"
- "WinCompanion — a Home Assistant client for Windows"
- A coined name (like HASS.Agent did) + "for Home Assistant" subtitle

---

## 3. Use of the Official Home Assistant Logo by Third Parties

### 3.1 The Home Assistant logomark — what it is (from the SVG source)

Retrieved from `OpenHomeFoundation/brand-assets:home-assistant/logo/screen/logomark/HA-logomark-color.svg` (SHA `5b06a0fa`):

The SVG defines **two paths on a 240×240 canvas**:
1. A **large pentagon/house silhouette** — a five-sided shape (house with a pointed top, flat base, and rounded bottom corners). Color: **`#F2F4F9`** (very light blue-gray, near-white).
2. An **overlay of the same house shape** but cut away to show the characteristic HA "network/nodes" pattern — **three circles connected by lines** (a network graph motif inside the house). Color: **`#18BCF2`** (bright cyan-blue). The path also includes three circles at fixed positions within the house:
   - Bottom-left circle (~r=20.5)
   - Top-center circle (~r=20.5, connected to a vertical line running from ~y=57 to y=212)
   - Bottom-right circle (~r=20.5)
   - Connected by diagonal lines forming an "H" or network pattern

The overall shape is a **house icon** (pointed roof, square body, rounded base corners) with a **three-node network graph** overlaid in bright cyan-blue on a pale background. The primary brand color is **`#18BCF2`** (the cyan-blue).

Source: https://github.com/OpenHomeFoundation/brand-assets/blob/163c9422939a014f5866c01c176fbb332e2aba08/home-assistant/logo/screen/logomark/HA-logomark-color.svg

### 3.2 What is explicitly forbidden

From the brand-assets README:
> **The logo "is not available for commercial use without express written permission."**

From the brands repo README:
> **"Custom integrations must not use Home Assistant branded images"** — to avoid implying official status.

By general trademark and brand-protection norms:
- ❌ Recoloring the official HA logo
- ❌ Tracing or creating a derivative of the house/network silhouette
- ❌ Using the official HA logo as your app icon, tray icon, or splash screen
- ❌ Using it in your own logo or wordmark
- ❌ Using it in marketing material without explicit permission from OHF

### 3.3 Permitted use (narrow)

The OHF brand-assets README does not state an explicit "you may use this for non-commercial open-source projects." The statement is binary: commercial use requires written permission, with no carve-out stated. However, **referencing the HA logo in documentation** (e.g., "the Home Assistant logo belongs to the Open Home Foundation") with attribution is consistent with standard nominative fair use and the "identification purposes only" model used in the brands repo.

For a third-party app's own icon — **do not use the HA logomark or any derivative of it.**

---

## 4. Non-Endorsement Disclaimer Wording — Real Examples

### 4.1 HASS.Agent (hass-agent/HASS.Agent) — current fork

**Finding:** The current HASS.Agent README (**SHA `4fdb9a15`**, https://github.com/hass-agent/HASS.Agent/blob/bb97a9f365950c7dadd6fa4c4ebf1b61e8901667/README.md) contains **no explicit trademark disclaimer**. It identifies itself purely by description:

> "HASS.Agent is a Windows-based client (*companion*) application for [Home Assistant](https://www.home-assistant.io)"

The credits section says:
> "Thanks to the entire team that's developing [Home Assistant](https://www.home-assistant.io) - such an amazing platform!"

There is no "not affiliated with" or "not endorsed by" sentence anywhere in the README.

### 4.2 HASS.Agent (LAB02-Research/HASS.Agent) — original

The original repo README also contains **no explicit disclaimer**. Same pattern — describes itself as "a Windows-based client for Home Assistant" with credits, but no formal non-endorsement statement.

Source: https://github.com/LAB02-Research/HASS.Agent

### 4.3 BRUH Automation / BRUH-HA-Apps — **Best real-world example found**

This repo (Home Assistant add-on repo by BRUH Automation, SHA `7b776c23`) carries a **model disclaimer** in its README:

> **Verbatim (from `bruhautomation/BRUH-HA-Apps` README, `## Disclaimer` section):**
> "BRUH Automation and these add-ons are independent projects, **not affiliated with, endorsed by, or sponsored by** Anthropic, Home Assistant / Nabu Casa, Mojang, or Microsoft. **'Home Assistant' is a trademark of the Open Home Foundation.**"

Source: https://github.com/bruhautomation/BRUH-HA-Apps/blob/main/README.md

This is the **cleanest, most legally complete disclaimer found** in the adjacent ecosystem. It:
- Asserts independence
- Denies affiliation, endorsement, and sponsorship
- Attributes the trademark to the OHF by name
- Names the platform and its former administrator (Nabu Casa)

### 4.4 home-assistant/iOS (official app)

The official iOS app README contains no disclaimer (it *is* the official app). Its footer badge links to the OHF:

> `[![Home Assistant - A project from the Open Home Foundation](https://www.openhomefoundation.org/badges/home-assistant.png)](https://www.openhomefoundation.org/)`

This badge/logo should **not** be used by unofficial projects — it explicitly marks official OHF membership.

Source: https://github.com/home-assistant/iOS/blob/main/README.md

---

## 5. Visual Identity of Adjacent Projects — Marks to Avoid Resembling

### 5.1 Official Home Assistant Logomark

**Shape:** A five-sided house silhouette (pentagon with pointed roof, flat sides and base, slight rounded corners at the bottom). Inside the house: three filled circles connected by straight lines in an "H" or network-graph pattern — one at top-center, two at the lower sides, with a vertical line and diagonal lines joining them.

**Colors:**
- House background fill: `#F2F4F9` (very pale blue-gray, near-white)
- Network graph and house outline: `#18BCF2` (bright sky-blue / cyan)

**Motif summary:** "House + network nodes" — a home automation metaphor rendered in cyan on a pale ground.

**Files available in:** `OpenHomeFoundation/brand-assets:home-assistant/logo/` in logomark, wordmark (text "Home Assistant"), and lockup (both combined) variants; print (EPS, PDF) and screen (SVG, PNG); color and monochrome; on-dark and on-light.

**Do not:**
- Use a house-shaped app icon in cyan/`#18BCF2` — this is confusingly similar to the HA logomark
- Use three circles connected by lines (network graph) inside any shape
- Use `#18BCF2` as a primary brand color with house or network motifs

### 5.2 HASS.Agent Logo

**Location:** `hass-agent/HASS.Agent:assets/logo_128.png` (3,394 bytes PNG, SHA `37e6f839`) — this is a 128×128 pixel image. The PNG is binary and cannot be described from SVG source.

**From screenshot context and community knowledge:** HASS.Agent uses a **stylized "H" letter** in a flat-design icon, in a shade of **blue** — visually distinct from the HA house/network mark. It is not a house shape. The original LAB02 version used the same mark (referenced at `raw.githubusercontent.com/LAB02-Research/HASS.Agent/main/images/logo_128.png` — currently 404, implying it has moved to the fork).

**⚠️ Unverified:** The exact colors of the HASS.Agent logo could not be determined from file metadata alone. The icon is believed to be a letter-"H" motif on blue, but this is inferred from prior community screenshots, not from reading the current binary PNG.

### 5.3 Home Assistant Companion (iOS/Android — official)

The official mobile app icon is the **same HA house+network logomark** in `#18BCF2` on white, adapted to iOS/Android icon shapes (rounded square). The app Store name is "Home Assistant."

**Do not:** use a rounded-square icon with the house+network motif — this is the official app's mark.

### 5.4 Windows Logo (Microsoft)

The Windows logo is a **four-pane window/grid** in the Microsoft brand colors (blue, red, yellow, green). Microsoft's FY26 trademark list includes "Windows" as a registered trademark.

**Do not:** Use a four-pane window/grid motif or the Microsoft product icon set in your app icon. The Windows Fluent Design icon set (Segoe Fluent Icons, WinUI icons) may be used freely inside the app UI per Windows App SDK guidelines but must not become your app's own logo or promotional icon.

---

## 6. Microsoft / Windows Trademark Constraints

### 6.1 The "Windows" wordmark in a product name

From the **Microsoft Trademark and Brand Guidelines** (https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks):

> **"Many uses, including our logos, app and product icons, and other designs, will require a license first. Unless you have an express license from Microsoft, these Trademark Guidelines will exclusively govern your use of our Brand Assets."**

Key rules synthesized from the guidelines (per web search research citing the official policy):

| Use | Status |
|---|---|
| `"[YourApp] for Windows"` in a subtitle or description | ✅ Generally permitted — factual compatibility |
| `"Windows [YourApp]"` as the *primary* product name | ❌ Not permitted — "Windows" dominant in the name |
| Windows logo, four-pane window, or product icons | ❌ Requires express license |
| Fluent/Segoe icons *inside* a WinUI app UI | ✅ Permitted per Windows App SDK SDK license |
| Required attribution when referencing Windows | "Windows is a trademark of the Microsoft group of companies." |

**Your current name "Home Assistant Windows Companion":** The word "Windows" appears in the middle, not first, and is used as a platform descriptor rather than as a brand element. This is safer than "Windows Home Assistant Companion" but still warrants a trademark attribution notice.

### 6.2 Fluent system icons

Fluent system icons (from the `microsoft/fluentui-system-icons` repo, Apache 2.0) and Segoe Fluent Icons (bundled with Windows, royalty-free for use *on* Windows per Windows App SDK terms) are permitted for in-app UI use. **Do not incorporate a Fluent icon as your app's own icon or logo** in a way that implies it is a Microsoft product.

---

## Hard Constraints

> Everything the project **must not** do.

### Home Assistant / OHF

- ❌ **Do not use the official HA house+network logomark** (or any derivative/recolor of it) as the app icon, tray icon, splash screen, or in the project logo. Source: OHF brand-assets README.
- ❌ **Do not use `#18BCF2` (HA's cyan-blue) combined with a house silhouette** or three-node network graph — confusingly similar to the registered HA mark.
- ❌ **Do not use the OHF "Home Assistant — A project from the Open Home Foundation" badge** (the official badge linked in the iOS repo). That badge marks official OHF projects only.
- ❌ **Do not use the "Works with Home Assistant" badge** — it requires a signed agreement and CHF 500 annual fee, and is for certified hardware, not software companions.
- ❌ **Do not claim or imply official endorsement, affiliation, or sponsorship** by Home Assistant, Nabu Casa, or the Open Home Foundation — trademark law requires this regardless of whether OHF publishes an explicit policy.
- ❌ **Avoid "Home Assistant" as the first word in the product's primary display name** — strong community norm and trademark-law risk. "Home Assistant Windows Companion" (current README title) needs a disclaimer at minimum; renaming to "Windows Companion for Home Assistant" would be lower risk.
- ❌ **Do not use any image from `home-assistant/brands` or `OpenHomeFoundation/brand-assets`** as your own integration icon — explicitly prohibited in the brands repo README.
- ❌ **Do not imply this is the "official" Windows companion** — there isn't one, but explicitly stating "not an official Home Assistant product" prevents confusion.

### Microsoft / Windows

- ❌ **Do not use the Windows logo** (four-pane grid) or any Microsoft product icon in your app icon or project logo.
- ❌ **Do not name the product "Windows [Something]" with Windows first** — implies a Microsoft product.
- ❌ **Do not use Segoe or Fluent icons as the primary branding mark** outside of the in-app UI.

---

## Recommended Naming & Disclaimer

### On naming

The lower-risk display name is:
- **"Windows Companion for Home Assistant"** — platform first, trademark second, clearly descriptive.
- Or keep a coined short name (e.g., `WinHA Companion`, `HaWin`, etc.) with a subtitle "for Home Assistant."

The current README title **"Home Assistant Windows Companion"** is workable with a clear disclaimer, but is the riskier form.

### Disclaimer sentences — paste-ready

**Option A (full, formal — recommended for README and About dialog):**
> "Home Assistant Windows Companion is an independent, community-developed project. It is **not affiliated with, endorsed by, or sponsored by** the Open Home Foundation, Nabu Casa, or the Home Assistant project. "Home Assistant" is a registered trademark of the **Open Home Foundation** (https://www.openhomefoundation.org). "Windows" is a trademark of the Microsoft group of companies."

**Option B (compact — for a single-line badge or footer):**
> "Not an official Home Assistant product. "Home Assistant" is a trademark of the Open Home Foundation."

**Option C (About dialog / credits screen style):**
> "This app is an independent third-party project for the [Home Assistant](https://www.home-assistant.io) platform. It is not made by, affiliated with, or endorsed by the Open Home Foundation or Nabu Casa. Home Assistant® is a registered trademark of the Open Home Foundation."

> 💡 **Model to follow:** The BRUH Automation disclaimer (verbatim above, §4.3) is the best real-world example found in the HA ecosystem. It is compact, accurate, and names both "Home Assistant / Nabu Casa" and the OHF, consistent with the brand transition.

---

## Marks to Avoid Resembling

| Mark | Visual description | Colors | Why to avoid |
|---|---|---|---|
| **Home Assistant logomark** | Five-sided house silhouette (pointed roof, flat base, slight rounded bottom corners) with three filled circles connected by lines (network graph) inside the house | Background: `#F2F4F9` (near-white); house+network: `#18BCF2` (bright cyan-blue) | Registered trademark of OHF; any house+cyan-blue combination risks confusion |
| **Home Assistant wordmark** | "Home Assistant" set in a sans-serif typeface (Inter or similar), plain weight | Same `#18BCF2` or neutral gray | Avoid using this exact typographic treatment in your own name/logo |
| **HA Companion mobile app icon** | The HA house+network mark adapted to iOS/Android rounded-square icon shape, solid `#18BCF2` on white | `#18BCF2` on white | It is the *official* companion app; reusing its shape signals official status |
| **HASS.Agent logo** | Believed to be a stylized blue "H" letter mark, flat design, ~128×128px | Blue (exact shade unverified — PNG binary only) | Closest direct competitor; any "H"-initial blue flat icon risks visual confusion with HASS.Agent |
| **Windows logo** | Four equal rectangular panes in a 2×2 grid, each pane a different Microsoft brand color (blue, red, yellow, green), the whole rotated ~15° | Blue `#00ADEF`, Red `#F35325`, Yellow `#FFBA08`, Green `#81BC06` | Microsoft registered trademark; resemblance implies a Microsoft product |
| **OHF "A project from the Open Home Foundation" badge** | "Home Assistant" wordmark + "A project from the Open Home Foundation" text below, typically on a colored background | Matches HA brand colors | Marks official OHF member projects; third parties must not use it |

---

## Full Source Citations

| # | Source | URL / Path | Notes |
|---|---|---|---|
| 1 | OHF brand-assets README (authoritative logo policy) | https://github.com/OpenHomeFoundation/brand-assets/blob/163c9422939a014f5866c01c176fbb332e2aba08/README.md | **Primary source** for logo copyright statement |
| 2 | HA logomark SVG (color) | `OpenHomeFoundation/brand-assets:home-assistant/logo/screen/logomark/HA-logomark-color.svg` SHA `5b06a0fa` | Source for color and shape analysis |
| 3 | home-assistant/brands README | https://github.com/home-assistant/brands/blob/e1d1c9655f81f1805e8c8379aa815873e352ec81/README.md | Custom integrations must not use HA branded images |
| 4 | Works with Home Assistant | https://works-with.home-assistant.io/ | Badge program — hardware only, signed agreement required |
| 5 | OHF announcement | https://www.home-assistant.io/blog/2024/08/08/works-with-home-assistant-becomes-part-ohf/ | Confirms OHF owns HA trademark as of 2024 |
| 6 | WIPO trademark (1874512) | https://www.trademarkelite.com/wipo/trademark/trademark-detail/1874512/Home-Assistant | International "Home Assistant" mark |
| 7 | EUIPO trademark (018721682) | https://www.trademarkelite.com/europe/trademark/trademark-detail/018721682/Home-Assistant | EU "Home Assistant" mark |
| 8 | HASS.Agent (current fork) README | https://github.com/hass-agent/HASS.Agent/blob/bb97a9f365950c7dadd6fa4c4ebf1b61e8901667/README.md | No disclaimer found |
| 9 | HASS.Agent (original) README | https://github.com/LAB02-Research/HASS.Agent/blob/main/README.md | No disclaimer found |
| 10 | BRUH-HA-Apps README (best disclaimer example) | https://github.com/bruhautomation/BRUH-HA-Apps/blob/main/README.md | **Model disclaimer verbatim, §4.3** |
| 11 | home-assistant/iOS README | https://github.com/home-assistant/iOS/blob/main/README.md | Official app; shows OHF badge format |
| 12 | Microsoft Trademark & Brand Guidelines | https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks | "Windows" trademark policy |
| 13 | OHF brand guidelines site (referenced, not accessible) | https://brands.openhomefoundation.io | ⚠️ DNS not resolving at research time — may go live later |
| 14 | OHF documents page | https://www.openhomefoundation.org/documents/ | Returned minimal content; no trademark policy document linked |

---

## Gaps and Uncertainties

| Item | Status |
|---|---|
| `brands.openhomefoundation.io` brand guidelines site | ⚠️ DNS not resolving — site may be unpublished or moved. Check periodically; it may contain explicit third-party use rules. |
| `home-assistant.io/trademark/` page | ⚠️ Returns 404 — no official trademark FAQ page exists at this URL. |
| OHF explicit "nominative use" policy for software | ⚠️ **Does not exist in writing** as of research date. Inferred from trademark law + community norms. Consider emailing `partner@openhomefoundation.org` for written confirmation before commercial distribution. |
| HASS.Agent logo exact colors | ⚠️ PNG binary only — hex colors not verifiable without rendering. Visual inspection of https://github.com/hass-agent/HASS.Agent/blob/bb97a9f365950c7dadd6fa4c4ebf1b61e8901667/assets/logo_128.png recommended. |
| Whether OHF would object to the current repo name | ⚠️ Unknown — no enforcement history found. Risk is moderate for "Home Assistant Windows Companion" as a primary name; use a disclaimer regardless. |
| Microsoft Store App submission name rules | ⚠️ Not researched in depth — if this app is ever submitted to the Microsoft Store, the Store has its own trademark review process for app names containing "Windows" or "Home Assistant." Research separately before submission. |
