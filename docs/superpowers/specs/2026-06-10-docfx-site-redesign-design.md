# DocFX Site Redesign & Documentation Expansion — Design

**Date:** 2026-06-10
**Status:** Approved

## Goal

Turn the RustPlusApi documentation site (DocFX, GitHub Pages) into a visually distinctive,
Rust-themed, well-detailed reference — and bring all repository READMEs in line with it.

## Scope

1. Custom Rust-themed dark visual identity for the DocFX site (template layer over `modern`).
2. Full content expansion: deepen the 8 existing articles, add 2 new ones, restructure the TOC.
3. Refresh all 7 READMEs (root, 4 packages, samples, docs) for consistent branding.

Out of scope: changing library code, CI/CD pipelines (the existing `Documentation.yml` workflow
keeps working unchanged), versioned docs, PDF output.

## 1. Visual identity — custom template layer

New folder `docs/template/` registered third in the `template` array of `docfx.json`
(`["default", "modern", "template"]`). Only overrides; the modern template keeps handling
search, TOC, layout and API pages.

- **`docs/template/public/main.css`**
  - Dark-first palette: deep charcoal-brown background (≈ `#171311` / `#1f1a17` surfaces),
    rust red `#CE412B` primary accent, amber `#e8a33d` highlights, warm gray text.
    Implemented by overriding the template's Bootstrap/docfx CSS variables for both
    `[data-bs-theme="dark"]` and light (light gets a warm paper tint, accent stays rust red).
  - Typography: Oswald (display, headings/nav brand), Inter (body), JetBrains Mono (code),
    loaded from Google Fonts via CSS `@import`.
  - Component styling: hero section, feature/package card grid with hover glow, rust-accented
    buttons, links, alerts, tables, code blocks.
- **`docs/template/public/main.js`**
  - Default theme = dark (respecting an explicit user choice in localStorage).
  - Mermaid initialization (dark theme) so articles can use ```` ```mermaid ```` blocks.
- `docfx.json` additions: template entry, `_appFooter` refresh; everything else stays.

## 2. Landing page (`docs/index.md`)

Rebuilt as a hero landing page using raw HTML styled by the template CSS:

- Hero: logo (`icon.png` copied to `docs/images/`), title, tagline, two CTA buttons
  (**Get Started**, **API Reference**), badge row.
- Feature grid: 6 cards (server control, team & clan, cameras, FCM notifications, native
  credentials, multi-targeting).
- Package cards: the 4 NuGet packages with shields.io NuGet badges and one-line descriptions.
- Quickstart code snippet + "where to next" links.

## 3. Content expansion

### Existing articles (deepened)

| Article | Additions |
| --- | --- |
| `introduction.md` | Package architecture mermaid diagram; clearer "how it fits together". |
| `getting-started.md` | Fuller step-by-step; prerequisites; what each credential value is. |
| `credentials.md` | Mermaid sequence diagram of the 8-step registration flow; expanded Chrome/upstream notes. |
| `rustplus-client.md` | Complete grouped method reference tables (server/world, entities, team, clan, nexus, camera, low-level); full event reference; connection lifecycle notes. |
| `clan-and-nexus.md` | Fuller ClanInfo/role model detail; event payloads. |
| `cameras.md` | Camera pipeline mermaid diagram (subscribe → frames → renderer → PNG); control-flags detail; identifier conventions. |
| `fcm-notifications.md` | Notification flow diagram; persistentIds guidance; reconnect strategy example. |
| `samples.md` | Per-sample walkthrough detail; flow diagram retained/upgraded. |

### New articles

- **`troubleshooting.md`** — FAQ: connection refused / wrong port, pairing notification never
  arrives, Chrome/Chromium not found (`CHROME_PATH`), Facepunch proxy, token/credential expiry,
  entity events not firing (subscribe-by-request rule), FCM timeouts.
- **`recipes.md`** — real-world snippets: auto-respond to an alarm (switch on a siren), save the
  server map to disk, minimal team-chat bot, camera snapshot loop, persisting credentials.

### TOC restructure (`docs/articles/toc.yml`)

Sectioned navigation:

- **Get Started** — Introduction, Getting Started, Credentials
- **Guides** — RustPlus Client, Clan & Nexus, Cameras, FCM Notifications
- **Resources** — Samples, Recipes, Troubleshooting

`docs/testing.md` (currently orphaned — in no TOC) moves into the site under a **Development**
top-level TOC entry alongside a short "building the docs" page derived from `docs/README.md`.
Top-level `docs/toc.yml` becomes: Articles, Development, API.

## 4. READMEs (7 files)

Shared conventions: centered header (title, tagline), badge rows, links to the docs site,
consistent section ordering. Package READMEs ship to NuGet, so they stay plain markdown
(centered `<div>` + tables only — no CSS).

| File | Treatment |
| --- | --- |
| `README.md` (root) | Full hero treatment; packages table; quickstart; docs links; credits. Largely good already — align wording/anchors with the new site. |
| `src/RustPlusApi/README.md` | Uniform structure: what it is → install → quickstart → feature summary → link to docs article + API reference. |
| `src/RustPlusApi.Fcm/README.md` | Same structure. |
| `src/RustPlusApi.Fcm.Registration/README.md` | Same structure. |
| `src/RustPlusApi.Camera/README.md` | Same structure. |
| `samples/README.md` | Aligned branding; link to the new Samples article. |
| `docs/README.md` | Updated layout table (template folder, articles); build instructions kept. |

## 5. Error handling / risks

- **Mermaid in modern template:** wired via `main.js`; if the documented extension point
  changed in DocFX 2.78, fall back to embedding the mermaid ESM init inline. Verified by
  rendering an article with a diagram.
- **CSS variable names are template-internal:** pin styling to the variables shipped with
  DocFX 2.78.5 (the version in CI is installed fresh — acceptable drift risk; verify after
  any future DocFX bump).
- **NuGet README rendering:** package READMEs avoid raw HTML beyond `<div align="center">`
  (NuGet strips most HTML); verified visually against NuGet's markdown subset rules.

## 6. Verification

- `docfx docs/docfx.json` builds clean (no broken xref/link warnings introduced).
- Serve locally, screenshot in browser: landing page, an article with mermaid, an API page —
  in both dark and light themes — and review the screenshots.
- Search still works; TOC sections render; mobile/narrow viewport sanity check.
