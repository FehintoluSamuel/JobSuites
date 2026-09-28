# JobSuites — Design System

Derived from the Executive Crimson mockups in `reference/mockups/`, corrected
against what the source data can supply and against the evidence model in
`ARCHITECTURE.md`.

---

## 1. Corrections applied to the reference

| Issue | Resolution |
|---|---|
| Spec prose specifies slate; mockups ship Material-3 periwinkle (`#eaedff`) | **Slate.** Crimson reads expensive on grey and muddy on lavender. Periwinkle tokens dropped |
| 5 gradients across the mockups | **None.** Flat fills only |
| `cursor-pointer` on 22 divs, 12 doing nothing | Every interactive element is a real control |
| Zero `role`, `tabindex`, `<label for>` in the whole set | Accessibility is a build requirement — §8 |
| 19 `<img>` using `data-alt` instead of `alt` | Enforced by the image component type |
| KPI cards assume a power user; new accounts see `0 / 0 / 0 / 0` | Real empty states — §7 |

**Motif:** flat surface, hairline, one crimson accent, typography doing the
work. No gradients, no glass, no glow.

---

## 2. Tokens

### Colour

```
canvas          #f8fafc      flat, no shadow
surface         #ffffff      1px #e2e8f0
surface-sunken  #f1f5f9      wells, table headers
border          #e2e8f0      hairline
border-strong   #cbd5e1      inputs, dividers

text            #0f172a      headings
text-body       #334155      body
text-muted      #64748b      metadata
text-faint      #94a3b8      placeholder only — fails contrast on white

accent          #e11d48      primary action, active nav, match signals
accent-hover    #be123c
accent-active   #9f1239
accent-wash     #fff1f2      ghost hover, row hover
accent-tint     #ffe4e6      selected row, match chip fill

brand           #881337      structural: rail, headers, governance actions
brand-deep      #4c0519

positive        #059669      deltas
negative        #b91c1c
warning         #b45309
```

Crimson is rationed. One primary action per view. If two things are crimson,
one is wrong.

Verdicts carry their own semantics — `STRONG` positive, `PARTIAL` warning,
`MISSING` negative, `WEAK` muted. Never crimson; crimson means *action*.

### Typography — Hanken Grotesk

```
display      36 / 44   700   -0.025em
headline-lg  28 / 36   700   -0.02em
headline-md  20 / 28   600   -0.015em
headline-sm  16 / 24   600   -0.01em
body-lg      16 / 24   400
body-md      14 / 20   400   ← default
body-sm      12 / 16   400   0.005em
label-md     13 / 18   600   0.01em
label-sm     11 / 14   700   0.05em  UPPERCASE
```

### Space, radius, elevation

```
space     4 / 8 / 12 / 16 / 20 / 24 / 32
radius    control 8 · card 8 · overlay 12 · pill 9999
rail      260px, collapses to 68px below 1280px
topbar    64px
grid      12 columns, 24px gutters
row       48px
```

**Elevation is border and tint, not shadow.** Two shadows exist, overlays only:

```
overlay    0 20px 25px -5px rgba(15,23,42,.10), 0 8px 10px -6px rgba(15,23,42,.05)
popover    0 4px 20px -2px rgba(15,23,42,.06), 0 2px 6px -1px rgba(15,23,42,.04)
```

Selected or urgent rows carry a `3px` left edge in `accent` or `brand`.

---

## 3. Imagery

**The source provides no imagery.** Verified: one `<img>` on a job page and it
is the board's own logo; `og:image` is a site-wide placeholder; no employer
website, so no domain to resolve a logo from. Recruiter photography has no
automated source — structural, not an effort gap.

So imagery is sourced, not harvested.

**Company identity**

| Case | Treatment |
|---|---|
| Logo available | 32–40px, `rounded-lg`, 1px `#e2e8f0` border, white plate |
| **No logo (expected for most SME listings)** | **Wordmark tile.** 1px hairline, `#f1f5f9` fill, company short name in `label-md` 600 `#0f172a` |

The wordmark tile is the primary treatment, not a fallback. Letters in a
coloured circle are excluded — they read as a loading error. A typeset wordmark
reads as a decision.

**People** — user-uploaded photos with an explicit consent toggle. Where none,
name in `headline-sm` with title in `body-sm text-muted`. A name and a role
carry more than an avatar placeholder. Never hotlink LinkedIn photos; the CDN
blocks it, so it renders in development and fails in production.

**Ambient** — licensed editorial photography (workspace, hands, city windows, a
desk mid-review) for empty states and onboarding. Warm, documentary, no staged
stock smiles.

**Contract**

```ts
type ImageRef = {
  url: string
  status: 'ok' | 'missing' | 'blocked'
  width: number; height: number
  alt: string          // mandatory
}
```

Intrinsic dimensions on every image. `status: 'missing'` renders the wordmark
tile. Lazy-load below the fold.

---

## 4. Match queue

The screen that decides whether the product is believed. It shows **why**, never
**how confident**.

```
┌──────────────────────────────────────────────────────────────────────┐
│  Field Service Engineer (Solar Energy)                ┌───────────┐  │
│  Jaza Energy · posted in 10 states                   │ wordmark  │  │
│                                                      └───────────┘  │
│                                                                      │
│  SKILLS MATCH          STRONG                                         │
│  "5+ years solar PV installation and inverter commissioning"        │
│  ↳ your CV: solar installation · inverter commissioning             │
│                                                                      │
│  LOCATION               EXACT                                         │
│  Lagos, Ogun, Ondo +7 · 4 in your preferred states                   │
│                                                                      │
│  SENIORITY              NEAR                                          │
│  asks 5+ years · you have 3                                           │
│                                                                      │
│  ── uncovered ────────────────────────────────────────────────       │
│  IFRS 15 · no evidence in your CV                    [ add to CV ]  │
└──────────────────────────────────────────────────────────────────────┘
```

**Verdicts are categorical.** `STRONG` / `PARTIAL` / `WEAK` / `MISSING`. Never a
percentage — a number implies precision the system does not have, and a
percentage that proves arbitrary is a trust liability the moment a user notices.

Every verdict carries the JD span that produced it and the CV facts it matched.
**A role that cannot cite evidence does not appear.**

The uncovered block is not an error state. It is the most useful part of the
screen: it tells the user exactly what to fix, and it makes clear the product
did not quietly paper over a gap.

The multi-location treatment is what clustering buys: one entry with ten
locations instead of ten entries that are the same job.

---

## 5. Tailoring workspace

The trust screen. The user is being handed a document that will be submitted
under their name, so every change is legible.

```
┌───────────────────────────┬──────────────────────────────────────────┐
│ MASTER CV                 │ TAILORED FOR THIS ROLE                   │
│                           │                                          │
│ Electrical Engineer       │ ELECTRICAL ENGINEER                      │
│ PZ Cussons · 2021–2026    │ PZ Cussons · 2021–2026        (unchanged)│
│                           │                                          │
│ • Drove reliability of    │ • Drove solar PV uptime from 71% to 94%   │
│   plant instrumentation   │   across 11 sites        ← reordered     │
│   ▸ see CV line 14        │   ◂ matches "PV performance"             │
│                           │                        [ why? ]         │
│ • Reports to Plant Lead   │ • Reports to Plant Lead      (unchanged) │
└───────────────────────────┴──────────────────────────────────────────┘

COVERAGE     4 of 6 must-have requirements covered        [ 67% ]
UNCOVERED    IFRS 15 · Project Finance (Prince2)
             No supporting evidence in your CV. We have not guessed.
             [ Add to CV ]  [ Leave out ]
```

Rules:

- **Every diff carries a reason and a source.** `[ why? ]` resolves to the CV
  line and the JD span.
- **Coverage is a real number** — covered must-haves over total must-haves,
  computed from the requirement table. Not a fit score.
- **Uncovered is stated, never hidden.** A tailored CV that omits an
  unmeetable requirement silently is worse than one that names it.
- **Verification badge.** If the fabrication check passed, a quiet
  `Verified · every claim traces to your CV` chip sits in the header. If it
  failed, the offending entity is named in plain language and the document is
  blocked from approval.
- **Nothing auto-sends.** The export is the end of the flow.

---

## 6. Interview prep

### Question review

Grouped by the gap it addresses, so the structure teaches something:

```
YOUR GAPS FOR THIS ROLE

▸ IFRS 15 — JD asks, your CV doesn't show it
  Q  "Walk me through an IFRS 15 revenue assessment you've led."
     Suggested frame: adjacent audit experience [CV line 31] + what you'd do
     differently under IFRS 15.
     ⚠ We will not suggest claiming experience you don't have.

▸ Solar PV scale-up — you have commissioning, not utility-scale
  Q  "Your CV shows 11 sites. How did you handle a site that missed its
     commissioning date?"
     Suggested frame: [CV line 22] — the Ogun site, 3 weeks late.
```

The honest-framing warning is a **product feature, not a disclaimer.** A
candidate coached into a lie gets caught in the interview and loses the job and
the product.

### Aptitude practice

Timed, per-domain, deterministic scoring. The interface states the domain
explicitly — `Numerical reasoning · 18 questions · 12:00` — because that is
how these assessments are actually administered and the rehearsal should match.

Weakest domains surface first. Scores use the same verdict machinery as
matching: `STRONG` / `PARTIAL` / `WEAK`, never a percentage.

---

## 7. Empty, error, and help states

Every state is a designed screen. New accounts start in all of them.

| State | Content |
|---|---|
| No profile | CV upload. One field, one button |
| No roles matched | Name the filters that excluded everything. Offer to widen one |
| Source stale | "Last checked 3 days ago", source named |
| Source degraded | Warning banner naming the source. Silent zero-yield is never acceptable |
| Multi-location role | Explicit. "Posted in 10 states" is information, not noise |
| Tailoring verification failed | The offending entity named. Document blocked from approval |
| No prep for this role | Offer to generate, or explain what is missing |
| Support | Ticket list with category, status, last activity. Inline form, no separate app |

The reference mockups showed populated dashboards with fabricated figures. Real
telemetry is mostly zero on day one, and the UI has to be honest about that.

**Support categories:** bug, data issue, account, feature request, other. A
`data issue` ticket is a data-quality signal, not just a support burden — it
feeds adapter health directly.

---

## 8. Density

Executive, high-density. `body-md` 14/20 default, 48px rows, tabular numerals.

Two exceptions where density costs comprehension:

- **Evidence spans are never truncated below two lines.** An unreadable
  verdict is worse than none.
- **The match queue is 10 items, not paginated.** It is a queue.

---

## 9. Accessibility — build requirements

The reference set had zero `role`, `tabindex`, and `<label for>` across 8 files.
Non-negotiable:

- Every control is a real `<button>`, `<a>`, `<input>`, or `<select>`. No div
  with a click handler.
- Every input has a `<label for>`, or `aria-label` where no visible label exists.
- Filter groups use `role="tablist"` / `role="tab"` / `aria-selected`.
- Toasts and match updates announce via `role="status"`, `aria-live="polite"`.
- Drag-and-drop has a keyboard path and `aria-grabbed` equivalents.
- Visible focus on everything: 1px `accent` + `0 0 0 3px rgba(225,29,72,.12)`.
- Contrast ≥ 4.5:1. `text-faint` fails on white and is placeholder-only.
- Do not hide the scrollbar globally, as the reference set does.
- Tailored documents must be readable by screen readers and selectable as text.
  A PDF that is only an image is a defect.
