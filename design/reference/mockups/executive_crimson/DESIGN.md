---
name: Executive Crimson
colors:
  surface: '#faf8ff'
  surface-dim: '#d2d9f4'
  surface-bright: '#faf8ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f3ff'
  surface-container: '#eaedff'
  surface-container-high: '#e2e7ff'
  surface-container-highest: '#dae2fd'
  on-surface: '#131b2e'
  on-surface-variant: '#5c3f40'
  inverse-surface: '#283044'
  inverse-on-surface: '#eef0ff'
  outline: '#906f70'
  outline-variant: '#e5bdbe'
  surface-tint: '#be0037'
  primary: '#b80035'
  on-primary: '#ffffff'
  primary-container: '#e11d48'
  on-primary-container: '#fffaf9'
  inverse-primary: '#ffb3b6'
  secondary: '#aa304f'
  on-secondary: '#ffffff'
  secondary-container: '#fd6f8c'
  on-secondary-container: '#6f0028'
  tertiary: '#6a5759'
  on-tertiary: '#ffffff'
  tertiary-container: '#846f71'
  on-tertiary-container: '#fffaff'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#ffdada'
  primary-fixed-dim: '#ffb3b6'
  on-primary-fixed: '#40000c'
  on-primary-fixed-variant: '#920028'
  secondary-fixed: '#ffd9dd'
  secondary-fixed-dim: '#ffb2bd'
  on-secondary-fixed: '#400014'
  on-secondary-fixed-variant: '#8a1538'
  tertiary-fixed: '#f7dcde'
  tertiary-fixed-dim: '#dac0c2'
  on-tertiary-fixed: '#26181a'
  on-tertiary-fixed-variant: '#544244'
  background: '#faf8ff'
  on-background: '#131b2e'
  surface-variant: '#dae2fd'
typography:
  display:
    fontFamily: Hanken Grotesk
    fontSize: 36px
    fontWeight: '700'
    lineHeight: 44px
    letterSpacing: -0.025em
  headline-lg:
    fontFamily: Hanken Grotesk
    fontSize: 28px
    fontWeight: '700'
    lineHeight: 36px
    letterSpacing: -0.02em
  headline-lg-mobile:
    fontFamily: Hanken Grotesk
    fontSize: 24px
    fontWeight: '700'
    lineHeight: 32px
    letterSpacing: -0.015em
  headline-md:
    fontFamily: Hanken Grotesk
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
    letterSpacing: -0.015em
  headline-sm:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '600'
    lineHeight: 24px
    letterSpacing: -0.01em
  body-lg:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
    letterSpacing: 0em
  body-md:
    fontFamily: Hanken Grotesk
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
    letterSpacing: 0em
  body-sm:
    fontFamily: Hanken Grotesk
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
    letterSpacing: 0.005em
  label-md:
    fontFamily: Hanken Grotesk
    fontSize: 13px
    fontWeight: '600'
    lineHeight: 18px
    letterSpacing: 0.01em
  label-sm:
    fontFamily: Hanken Grotesk
    fontSize: 11px
    fontWeight: '700'
    lineHeight: 14px
    letterSpacing: 0.05em
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1rem
  gutter-lg: 1.5rem
  margin: 1rem
  margin-md: 1.5rem
  margin-lg: 2rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 0.75rem
  space-lg: 1.25rem
  space-xl: 2rem
---

## Brand & Style

This design system establishes an authoritative, high-density environment tailored for enterprise recruitment leadership, talent executives, and high-performance hiring teams. It balances corporate discipline with surgical modern precision. 

The aesthetic is anchored in Modern Corporate High-Contrast: structural integrity, razor-sharp information hierarchies, high data density, and calculated pops of energetic signal color. The interface projects prestige, decisiveness, and institutional trust, avoiding frivolous decoration in favor of typographic rigor, disciplined borders, and purposeful contrast.

## Colors

The palette leverages a tri-tier red architecture supported by a slate-driven neutral scale:

- **Deep Burgundy (`#881337` / `#4c0519`)**: Represents structural authority. Deployed across executive navigation anchors, prominent data labels, high-level structural metrics, and primary headline accents.
- **Vibrant Crimson (`#e11d48`)**: Serves as the primary operational trigger. Reserved exclusively for key interactive states, high-priority match percentages, primary action buttons, active navigation indicators, and immediate calls-to-action.
- **Rose Mist (`#ffe4e6` / `#fff1f2`)**: Utilized for low-contrast spatial tinting, active selection backdrops, high-priority chip fills, and subtle focus glows.
- **Slate Neutrals**: 
  - Canvas Background: `#f8fafc`
  - Elevated Container / Card Surface: `#ffffff`
  - Hairline Structural Borders: `#e2e8f0`
  - Secondary Stroke & Dividers: `#cbd5e1`
  - Body Copy & Metadata: `#334155`
  - High-Contrast Heading & Primary Text: `#0f172a`

## Typography

The type system is unified under **Hanken Grotesk**, delivering geometric clarity alongside enterprise legibility. 

- **Display & Large Headlines**: Set with tight letter-spacing (`-0.025em` to `-0.02em`) and heavy weights (`700`) to anchor dashboard overviews and executive KPIs.
- **Section Headers & Small Headlines**: Use semi-bold cuts (`600`) to establish clear tabular and card boundaries without visual fatigue.
- **Body & Data Grid Text**: Leverages a dense `14px` default size with a rigid `20px` line-height to maximize vertical information scanning efficiency.
- **Micro-labels & Status Identifiers**: Set in `11px` bold with positive tracking (`0.05em`) in full uppercase where applied to badge pills, table headers, and metric categorization tags.

## Layout & Spacing

The application utilizes a fixed-width docked rail model combined with a fluid data canvas:
- **Left Navigation Rail**: Fixed at `260px` width (collapsible to `68px` icon rail on low-resolution viewports).
- **Top Command Bar**: Fixed at `64px` height, anchored along the top boundary spanning the primary work area.
- **Work Area Canvas**: Multi-column fluid grid system (12 columns on desktop) with `gutter-lg` (`24px`) spacing between core functional cards and tabular panels.
- **Information Density Rhythm**: Internal card components utilize an `8px` modular spacing cadence (`space-xs` = 4px, `space-sm` = 8px, `space-md` = 12px, `space-lg` = 20px) to maximize above-the-fold executive telemetry.

## Elevation & Depth

Visual hierarchy is communicated primarily through **low-contrast architectural outlines and subtle ambient tints**, foregoing heavy drop shadows:

- **Level 0 (Canvas Base)**: `#f8fafc` flat background.
- **Level 1 (Card & Module Surfaces)**: Solid `#ffffff` surface delineated by a `1px` crisp border in `#e2e8f0`. No base shadow.
- **Level 2 (Active/Hover Cards, Popovers, Flyouts)**: Retains `#ffffff` background with an intensified border (`#cbd5e1`) and an ultra-diffused, cool slate shadow: `0 4px 20px -2px rgba(15, 23, 42, 0.06), 0 2px 6px -1px rgba(15, 23, 42, 0.04)`.
- **Level 3 (Command Palette, Executive Drawers, Modals)**: Supported by a semi-transparent scrim (`rgba(15, 23, 42, 0.45)`) with an elevated surface casting a balanced ambient shadow: `0 20px 25px -5px rgba(15, 23, 42, 0.1), 0 8px 10px -6px rgba(15, 23, 42, 0.05)`.
- **Accent Elevation**: Focused active panels apply an inner hairline highlight or an adjacent active crimson spine (`3px` left edge border in `#e11d48`).

## Shapes

The design uses a clean, controlled corner radius strategy balancing modern software expectations with data-dense efficiency:

- Standard structural containers, data cards, and tabular panels employ `rounded-lg` (`0.5rem` / `8px`).
- Higher-level modular overlays, executive candidate dossier cards, and flyout surfaces utilize `rounded-xl` (`0.75rem` / `12px`).
- Form controls, action buttons, and input fields adopt `0.5rem` (`8px`) for geometric alignment.
- Tags, status indicators, and pill badges use full pill geometry (`9999px`) to create clear differentiation between actionable surfaces and informational labels.

## Components

### Buttons
- **Primary Action**: Solid `#e11d48` background with crisp `#ffffff` text. Hover state shifts to `#be123c`. Active state scales down imperceptibly (`scale(0.99)`) with `#9f1239`. Corner radius: `8px`.
- **Executive Secondary**: Solid `#881337` background with `#ffffff` text, used for primary governance/board-level actions. Hover state transitions to `#4c0519`.
- **Tertiary / Ghost**: Transparent background, `1px` border in `#e2e8f0`, `#334155` text. On hover, background shifts to `#fff1f2` with `#e11d48` text and border.

### Candidate & Metric Chips
- **High-Priority Match Tag**: Background `#ffe4e6`, text `#881337`, border `1px` solid `#fecdd3`. Typography: `label-sm` uppercase.
- **Neutral Attribute Chip**: Background `#f1f5f9`, text `#475569`, border `1px` solid `#e2e8f0`.

### Form Inputs & Filters
- **Text Inputs & Selects**: Height `40px`, background `#ffffff`, border `1px` solid `#cbd5e1`, text `#0f172a`, placeholder text `#94a3b8`. 
- **Focus State**: `1px` solid `#e11d48` accompanied by an ambient ring: `box-shadow: 0 0 0 3px rgba(225, 29, 72, 0.12)`.

### Checkboxes & Radios
- Box dimensions: `16px x 16px`, `4px` border radius (`rounded-sm`). Border `1.5px` solid `#94a3b8`.
- Checked state: `#e11d48` fill with sharp `#ffffff` check icon.

### Cards & Panels
- **Candidate Dossier Card**: Solid `#ffffff` base, `1px` border `#e2e8f0`, `20px` internal padding (`space-lg`). Features an optional left status accent border (`3px` solid `#881337` or `#e11d48`) indicating workflow phase or match urgency.
- **Metric KPI Widget**: High-density card featuring bold numerical figures in `#0f172a` (`28px`), coupled with trend deltas highlighted in `#e11d48` or `#059669`.

### Data Table Rows
- Row height: `48px`. Baseline border: `1px` solid `#f1f5f9`. 
- Hover state: Background `#fff1f2` with subtle cursor accent. Active/Selected state: Background `#ffe4e6` with `#881337` high-contrast text.