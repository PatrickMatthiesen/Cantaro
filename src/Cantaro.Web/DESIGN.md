---
name: Cantaro
description: A calm personal archive for syncing music and tracking media across platforms.
colors:
  archive-violet: "#7c3aed"
  archive-indigo: "#4f46e5"
  action-ink: "#0f172a"
  canvas-lavender: "#f7f5ff"
  soft-wash: "#eeeaff"
  panel-white: "#ffffff"
  panel-border: "#e7e2f7"
  field-border: "#e3def8"
  text-ink: "#0f172a"
  text-muted: "#64748b"
  dark-canvas: "#090d18"
  dark-panel: "#161d2e"
  success: "#047857"
  info: "#0369a1"
  warning: "#b45309"
  danger: "#be123c"
  brand-50: "#fbe0de"
  brand-500: "#d76869"
  brand-900: "#773737"
typography:
  display:
    fontFamily: "Space Grotesk, Inter, SF Pro Display, system-ui, sans-serif"
    fontSize: "3rem"
    fontWeight: 900
    lineHeight: 1.1
    letterSpacing: "-0.04em"
  headline:
    fontFamily: "Space Grotesk, Inter, SF Pro Display, system-ui, sans-serif"
    fontSize: "2rem"
    fontWeight: 900
    lineHeight: 1.15
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Space Grotesk, Inter, SF Pro Display, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 900
    lineHeight: 1.25
  body:
    fontFamily: "Space Grotesk, Inter, SF Pro Display, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 500
    lineHeight: 1.5
  label:
    fontFamily: "Space Grotesk, Inter, SF Pro Display, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 900
    lineHeight: 1.2
    letterSpacing: "0.22em"
rounded:
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  xxl: "28px"
  panel: "32px"
  pill: "9999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  2xl: "24px"
  3xl: "32px"
  page: "40px"
components:
  button-primary:
    backgroundColor: "{colors.action-ink}"
    textColor: "{colors.panel-white}"
    rounded: "{rounded.lg}"
    padding: "12px 20px"
    typography: "{typography.body}"
  button-accent:
    backgroundColor: "{colors.archive-violet}"
    textColor: "{colors.panel-white}"
    rounded: "{rounded.lg}"
    padding: "12px 20px"
    typography: "{typography.body}"
  button-soft:
    backgroundColor: "{colors.panel-white}"
    textColor: "{colors.text-ink}"
    rounded: "{rounded.lg}"
    padding: "12px 20px"
    typography: "{typography.body}"
  card-glass:
    backgroundColor: "{colors.panel-white}"
    textColor: "{colors.text-ink}"
    rounded: "{rounded.panel}"
    padding: "24px"
  input-search:
    backgroundColor: "{colors.panel-white}"
    textColor: "{colors.text-ink}"
    rounded: "{rounded.lg}"
    padding: "12px 16px 12px 44px"
    typography: "{typography.body}"
  chip:
    backgroundColor: "{colors.soft-wash}"
    textColor: "{colors.archive-violet}"
    rounded: "{rounded.pill}"
    padding: "6px 12px"
    typography: "{typography.label}"
---

# Design System: Cantaro

## 1. Overview

**Creative North Star: "The Living Personal Archive"**

Cantaro's current visual system is a soft product shell for personal collections: pale lavender canvases, translucent white panels, violet selection states, dark slate actions, cover art, and calm library language. It should feel like years of collected music and watched stories have become browsable and alive, not like another streaming service trying to own the collection.

The system is product-first. Navigation, search, tables, filters, provider states, and review queues must stay predictable and fast, while the atmosphere can carry a little warmth through artwork, softened panels, and careful copy. The magic belongs in rediscovery and confidence, not in decorative effects that compete with the task.

It explicitly rejects YouTube, Spotify, AniList, Trakt, generic SaaS dashboards, business/enterprise gray, and workplace/workspace metaphors. Cantaro is the canonical layer between services; external providers are endpoints, never the visual model.

**Key Characteristics:**
- Pale lavender app canvas with white translucent panels and slate text.
- Violet/indigo active states used for current location, focus, and important provider actions.
- Large, soft radii on existing cards and controls, with tighter radii reserved for dense rows and inputs.
- Artwork-led music and media moments that make existing collections feel worth revisiting.
- Direct product copy: friendly enough to be personal, precise enough to be trusted.

## 2. Colors

The palette is a restrained lavender-and-slate product system with violet as the active signal and semantic colors for sync, provider, warning, and destructive states.

### Primary
- **Archive Violet**: Primary accent for active navigation, selected controls, focus rings, connected actions, and key links.
- **Archive Indigo**: Gradient partner for active nav and dark-mode accent surfaces. Use with Archive Violet only when a directional state or music/media hero needs more depth.
- **Action Ink**: Primary command background for trusted actions such as Play, Save, Search, and Open. It keeps product actions grounded and prevents the whole UI from becoming purple.

### Secondary
- **Soft Wash**: Lavender control beds, segment tracks, badges, and low-emphasis selected states.
- **Brand Rose Ramp**: The OKLCH brand ramp exists in `src/index.css` as `--color-brand-50` through `--color-brand-900`. Use it sparingly for future brand moments or warm collection accents, not as the default product action color.

### Tertiary
- **Media Artwork Gradient**: Posters, album art, and provider-specific colors may create richer local moments. They should be contained inside artwork, hero panels, or media cards, not spread across chrome.

### Neutral
- **Canvas Lavender**: Default app surface for authenticated pages.
- **Panel White**: Cards, top bars, search surfaces, and dialogs.
- **Panel Border**: Soft borders around glass panels, sidebars, and settings surfaces.
- **Text Ink**: Primary text.
- **Text Muted**: Secondary text, metadata, helper copy, and table labels.
- **Dark Canvas** and **Dark Panel**: Dark-mode base colors, with muted violet/blue washes only where they preserve contrast.

### Named Rules
**The Cantaro Owns It Rule.** Provider brand colors may appear in icons, provider cards, and connection states, but they must never define the surrounding page.

**The Violet Is State Rule.** Violet is for selection, focus, connection, and current location. If a decorative element can be removed without changing meaning, it should not be violet.

**The No Enterprise Gray Rule.** Neutral surfaces must remain lavender-tinted or slate-anchored; flat gray dashboards are prohibited.

**The Color Must Earn It Rule.** Color must identify an action, interaction, state, category, or provider context. Keep static structure and inactive content neutral so primary actions, contextual hover states, progress, sync, warnings, and provider destinations can become expressive without making the page noisy. Do not assign unrelated accent colors to headings, metadata, or containers for decoration.

## 3. Typography

**Display Font:** Space Grotesk with Inter, SF Pro Display, system-ui, sans-serif fallbacks.
**Body Font:** Space Grotesk with Inter, SF Pro Display, system-ui, sans-serif fallbacks.
**Label/Mono Font:** Labels use the same family with heavy weights and tracking; durations and indexes use Tailwind's mono stack.

**Character:** The type is compact, friendly, and slightly technical. Heavy weights give the product confidence, while small labels and metadata keep dense screens scannable.

### Hierarchy
- **Display** (900, 3rem, 1.1): Page heroes and major collection titles. Keep letter spacing at `-0.04em` or looser.
- **Headline** (900, 2rem, 1.15): Settings sections, empty-state titles, provider panels, and page headers.
- **Title** (900, 1.125rem, 1.25): Card headings, sidebar feature cards, panel titles, and table section titles.
- **Body** (500-700, 0.875rem-1rem, 1.5-1.75): Descriptions, form copy, row text, and provider explanations. Prose should stay under 75ch.
- **Label** (900, 0.68rem-0.75rem, tracked uppercase): Sidebar section labels, table headers, eyebrows, and status labels. Use sparingly; repeated uppercase kickers on every surface make the app feel scaffolded.

### Named Rules
**The One Family Rule.** Product UI stays in the configured sans stack. Do not introduce display fonts for buttons, labels, inputs, or data.

**The Heavy With Restraint Rule.** Heavy `font-black` titles are part of Cantaro, but long prose and metadata must use medium or semibold weights so screens do not shout.

## 4. Elevation

Cantaro currently uses a hybrid of translucent panels, soft borders, backdrop blur, and diffuse shadows. Depth should imply a useful layer: navigation chrome, player, dialog, hero, active card, or toast. Flat tonal layering is preferred for dense tables and filters; large ambient shadows should be reserved for artwork, overlays, and persistent floating UI.

### Shadow Vocabulary
- **Glass Surface** (`0 8px 32px rgba(0,0,0,0.06), inset 0 1px 0 rgba(255,255,255,0.9)`): Shared glass cards and sync panels.
- **Panel Lift** (`0 20px 60px rgba(88,74,150,0.08)`): Sidebar footer cards and secondary panels.
- **Table Lift** (`0 20px 70px rgba(88,74,150,0.07)`): Dense music tables and suggestion panels.
- **Hero Depth** (`0 28px 90px rgba(88,74,150,0.12)`): Collection hero surfaces where artwork and context need stronger depth.
- **Floating Player** (`0 24px 80px rgba(55,45,120,0.16)`): Persistent player and high-priority floating controls.
- **Dialog Depth** (`0 32px 120px rgba(2,6,23,0.45)`): Modal and cropper overlays only.

### Named Rules
**The Layer Must Earn It Rule.** Shadows belong to actual layers: card, hero, player, drawer, dialog, toast. Do not add glow to ordinary text, icons, or inactive controls.

**The Glass Budget Rule.** Glass is already a major visual material. New glass surfaces must serve hierarchy, not decoration.

## 5. Components

### Buttons
- **Shape:** Soft rectangles and pills depending on context (`16px`, `24px`, or `9999px` radius).
- **Primary:** Action Ink background with white text and heavy body typography. Use for trusted actions such as Save, Play, Search, and Open.
- **Accent:** Archive Violet background with white text. Use for connect, selected, and high-affordance provider actions.
- **Hover / Focus:** Hover darkens or slightly shifts background; focus uses visible violet rings. Avoid scale effects except on media cards and small avatar/image affordances.
- **Secondary / Ghost / Tertiary:** White or translucent white backgrounds with slate text; hover raises clarity by becoming more opaque.

### Chips
- **Style:** Rounded pills with lavender or semantic backgrounds, heavy small text, and compact padding.
- **State:** Chips communicate platform, provider, sync, availability, warning, and current playback states. Color must be backed by text or icon meaning.

### Cards / Containers
- **Corner Style:** Existing surfaces commonly use very soft radii (`24px` to `32px`). Dense tables and rows tighten to `12px` to `28px`.
- **Background:** White translucency over Canvas Lavender, often with `backdrop-blur`.
- **Shadow Strategy:** Use Glass Surface, Panel Lift, or Table Lift based on actual layer depth.
- **Border:** Prefer soft full borders using Panel Border or white alpha. No colored side stripes.
- **Internal Padding:** Compact panels use `12px` to `16px`; cards and settings sections use `20px` to `32px`.

**The Border Budget Rule.** Prefer proximity, spacing, alignment, and tonal surface changes to communicate grouping. Add a divider or perimeter border only when removing it would make hierarchy or ownership ambiguous; do not outline every row, subsection, or nested region by default.

### Inputs / Fields
- **Style:** Rounded rectangles (`16px`), white or translucent white background, soft lavender border, slate text, medium weight.
- **Focus:** Border shifts to violet and may add a soft violet ring on form-heavy surfaces.
- **Error / Disabled:** Error uses rose background/text/border with a clear message. Disabled states reduce opacity and preserve layout size.

### Navigation
- **Style:** App-level navigation uses rounded segmented links; active state is a violet/indigo gradient with white text. Side navigation uses white selected rows with compact icons in lavender-bordered squares.
- **Typography:** Navigation labels are heavy and compact; sidebar section headings are uppercase tracked labels.
- **Mobile:** The sidebar collapses into a focused drawer with overlay, Escape close, focus trapping, and a restored trigger focus target.

### Segmented Switch
The shared segmented switch is a signature control for search modes and filters: translucent outer shell, lavender inset track, dark selected indicator, and heavy labels. Keep the indicator movement short and state-driven.

### Music Track Table
Music tables are dense but warm: translucent table shell, uppercase header row, album artwork thumbnails, hover playback affordances, and a violet now-playing state. Do not make tables look like enterprise grids.

### Media Library Card
Media cards are artwork-led with poster aspect ratios, dark gradient overlays, progress metadata, and status badges. The card hover scale is acceptable because it is attached to artwork browsing, not routine form controls.

**The Artwork Is the Affordance Rule.** Clickable posters, covers, and banners should communicate direct manipulation through a restrained hover or focus treatment such as a slight scale, crop shift, saturation change, focus ring, or contextual overlay icon. Do not add persistent instructional text such as “open artwork” when the image itself is the control. Keep an accessible name and an equally clear keyboard focus state; touch behavior must not depend on hover.

## 6. Do's and Don'ts

### Do:
- **Do** keep Cantaro's default app surface on Canvas Lavender with Panel White layers and slate text.
- **Do** use Archive Violet for active state, selection, focus, provider connection, and current location.
- **Do** make sync state, provider conflicts, unavailable tracks, and media progress visible with text, not color alone.
- **Do** let album art, posters, and provider icons create local richness while keeping page chrome provider-independent.
- **Do** use skeletons, empty states, and review queues that explain what the user can do next.
- **Do** preserve keyboard focus states and reduced-motion alternatives for every interactive pattern.

### Don't:
- **Don't** make Cantaro look or feel like YouTube, Spotify, AniList, Trakt, or any other connected platform clone.
- **Don't** make the product cold, business-like, enterprise, workplace, workspace, gray, or boring.
- **Don't** use generic SaaS dashboard tropes, hero metric blocks, gradient text, colored side-stripe cards, or repeated uppercase eyebrows as scaffolding.
- **Don't** hide complexity behind vague polish; sync, matching, provider state, and review differences must stay understandable.
- **Don't** add glassmorphism as decoration. Glass is allowed only when it clarifies layers.
- **Don't** pair 1px borders with huge decorative shadows on routine cards. Pick a restrained layer treatment that matches the component's importance.

## 7. Semantic Theme API

The shared Tailwind theme contract lives in `src/Cantaro.ClientShared/src/theme.css` and is imported by both the web app and browser-extension popup. Theme selection continues to set `data-theme="light"` or `data-theme="dark"` on the document root; the shared stylesheet registers that selector as Tailwind's `dark` custom variant and sets the matching native `color-scheme`.

Use the same utility names in every theme:

- **Surfaces:** `bg-canvas`, `bg-surface`, `bg-surface-translucent`, `bg-surface-raised`, `bg-surface-subtle`, and `bg-surface-hover`.
- **Content:** `text-content`, `text-content-muted`, `text-content-subtle`, and `text-content-inverse`.
- **Borders and focus:** `border-border-subtle`, `border-border-strong`, and `ring-focus`/`outline-focus`.
- **Selection and emphasis:** `bg-accent`, `bg-accent-soft`, `text-accent`, and `text-accent-strong`.
- **Primary actions:** `bg-action`, `hover:bg-action-hover`, and `text-action-content`. These remain a deliberately high-contrast pair in both themes; do not derive button backgrounds from `content`.
- **Personal accent:** `bg-personal-accent`, `hover:bg-personal-accent-hover`, `text-personal-accent-strong`, and `text-personal-accent-content` provide the restrained warm accent for playback, progress, and collection emphasis. Use the stronger text role on light surfaces; keep the role semantic so it can become user-configurable without changing component markup.
- **Statuses:** use the `*-surface`, `*-content`, and `*-border` roles for `info`, `success`, `warning`, and `danger`.
- **Destructive actions:** `bg-danger-action`, `hover:bg-danger-action-hover`, and `text-danger-action-content`. Filled controls must not use a status text color as their background.

Provider brand colors, album/poster artwork, chart or visualization palettes, image scrims, and intentionally theme-specific optical treatments remain bespoke. Authored dark CSS is acceptable for those cases, but it must target the component directly; broad selectors that reinterpret arbitrary Tailwind palette classes are prohibited.
