# JobSuites — Product Requirements

**The product is the suite:** ingest roles → match a CV against a job
description → tailor the CV to that job → prepare for that job's interview →
track the application. Three of those five steps are the same computation
applied to different questions, which is why they belong in one product and
one data model.

---

## 1. Problem

A candidate opens four boards, runs the same search on each, re-reads
near-identical descriptions, rewrites their CV by hand for each role, and walks
into interviews having prepared generically rather than for the role in front
of them. Listings repeat across states. Contact details are gated. Compensation
is almost never stated.

Every one of those steps is mechanical. The candidate should be spending their
attention on judgement, not on paperwork.

---

## 2. Users

**Primary — active job seeker.** 3–15 years experience. Fields observed in the
source data: banking and finance, engineering and technical, ICT, administration,
HR, sales. Uses a phone more often than a laptop. Needs fit, not volume.

**Secondary — recruiter or coach** managing several candidates. Out of MVP.

---

## 3. The evidence base

Everything downstream rests on this, and it is the reason the product is more
than three separate tools stapled together.

A CV is decomposed once into **addressable, normalised facts**, each pointing
back at the exact source text it came from:

```
FACT ──► SPAN (verbatim CV text) ──► CV
```

```
cv_fact {
  type        skill | employer | title | date_range | education | certification | language
  value       "SQL"
  normalised  "sql"
  span_id     → the sentence it came from
  recency_years
}
```

The same structure is extracted from every job description:

```
jd_requirement {
  category    skill | experience | education | certification | language | domain
  text        "5+ years with SQL and Power BI"     ← verbatim JD span
  must_have   bool
  years_min   int
}
```

**Three products, one join.**

| Product | Question | Operation |
|---|---|---|
| **Matching** | Does this CV fit this JD? | join `cv_fact` ↔ `jd_requirement` |
| **CV tailoring** | Which of my experience answers this JD? | select facts, reorder, reframe |
| **Interview prep** | What will they ask, given the gaps? | requirements with no supporting fact |

Because all three read the same facts, matching already tells tailoring which
facts to promote, and tells prep which gaps to drill. **Prep costs almost
nothing extra to build** once matching exists — this is why it belongs in the
same product.

The source text is non-negotiable. Every claim the product makes about a user,
and every claim a tailored document makes on their behalf, must be traceable to
a `span_id`. That is what makes the output trustworthy.

---

## 4. Profile

| Field | Source | Required |
|---|---|---|
| Name, email, location | signup | yes |
| CV text | upload → parsed into facts | yes |
| Skills | extracted, **user-correctable before first use** | yes |
| Target roles | user selects from the board's own taxonomy | yes |
| Years of experience | user input | yes |
| Preferred states | multi-select, 36 states + FCT | yes |
| Job types | full-time / contract / internship / part-time | yes |

An uncorrectable extraction silently destroys trust in every downstream
feature. The correction step is not optional.

---

## 5. Matching

### 5.1 Division of labour

The model **extracts and translates**. The code **decides**.

```
JD ──► LLM (structured output) ──► jd_requirement[]     normalise + extract
CV ──► LLM (structured output) ──► cv_fact[]            normalise + extract
                     │
                     ▼
             deterministic join ──► per-requirement verdict + evidence
                     │
                     ▼
              optional LLM rationale ──► prose only, never the verdict
```

Fit is **not** a model judgement. It is a set of lookups against
`synonym_normalised(fact) ↔ requirement.normalised`. Same inputs always give the
same verdict, and the verdict can always be traced to source text. This is
cheaper, faster, and auditable — and it is the thing that distinguishes this
from the free matcher the source already ships.

### 5.2 Verdicts

Categorical, never a percentage. A number implies precision the system does not
have; a percentage that turns out arbitrary is a trust liability the moment a
user notices.

| Verdict | Condition |
|---|---|
| `STRONG` | ≥2 supporting facts, one within 2 years |
| `PARTIAL` | exactly 1 supporting fact |
| `WEAK` | only adjacent/normalised-neighbour facts |
| `MISSING` | no supporting fact |

**A role that produces no evidence-bearing verdict is not shown.** If the user
cannot see why a role is in their queue, it should not be in their queue.

### 5.3 What is deliberately absent

**Compensation.** No employer-stated salary exists anywhere in the source — the
30% coverage widget is a company-level estimate derived from headcount, and it
repeats identically across every role at the same company. There is nothing to
filter on. Removed from matching until a source states it.

### 5.4 The comparison point

The source ships a free CV↔JD matcher (`myjobmag.com/cv-match`) on the same
listings. JobSuites must beat it on things it structurally cannot do:

- **Role clustering.** One role posted across 10 states is one entry with ten
  locations. Measured: 13 postings = 4 roles.
- **Explainability.** Evidence-linked verdicts instead of an opaque score.
- **Continuation.** Match → tailor → prep → track is one object. Their matcher
  ends at the score.

---

## 6. CV tailoring

### 6.1 What tailoring is allowed to do

Selection, ordering, and reframing of **existing true facts**. Nothing else.

```
must_have requirement ──► facts that answer it
                              │
                              ├─ SELECT    which facts appear
                              ├─ ORDER     which leads the document
                              └─ REFRAME   wording, using only source facts
```

The model never gains the ability to assert a fact. It selects from a closed
set. This is an architectural constraint, not a prompt instruction — a prompt
instruction is a request, a closed set is a guarantee.

### 6.2 Fabrication check

After generation, a **deterministic** pass — not a model judgement — extracts
every entity from the output (companies, numbers, dates, technology nouns) and
diffs it against the CV corpus. Any entity absent from the source is a
fabrication.

```
generated:  "Led a team of 12 engineers at Flutterwave"
CV facts:   [no Flutterwave]  [no 12]  →  REJECT, surface to user
```

This is the feature. "ChatGPT writes your CV" is a commodity; **"we can prove we
didn't invent anything"** is a product. It is also cheap: string matching over a
fact table, no model call.

### 6.3 Gap surface

Requirements with no supporting fact are **shown, not hidden and not invented**:

```
Uncovered:  IFRS 15 revenue recognition
            No evidence in your CV. Add it, or leave it out — we won't guess.
```

A tailored CV that silently omits a requirement the user cannot meet is worse
than one that names the gap.

### 6.4 Review and export

Side-by-side against the master CV, inline diff of what changed and why, every
change traceable to a fact, user approval required before anything is used.
Export DOCX and PDF.

Nothing is ever dispatched automatically. The user takes the document.

---

## 7. Interview preparation

Added because it falls out of the evidence base at near-zero marginal cost, and
because it closes the loop: the gaps matching already found are exactly what
gets asked about.

### 7.1 Tier 1 — Gap-driven questions (build first)

Given a role and that user's specific gaps, generate the questions actually
likely to be asked, and how to answer them honestly.

> JD requires IFRS 15. Your CV shows three years of audit but no IFRS.
> Expect: *"Walk me through an IFRS 15 revenue assessment you have led."*
> Honest framing: adjacent transfer from [audit experience], plus what you'd do
> differently under IFRS 15. **Not** a claim of IFRS experience.

Content is **keyed by role and cached**, so generation is paid once and served
to every user whose gaps match. This is what makes it affordable at zero budget.

This is the part no generic prep site can do. LeetCode has questions. Only this
product has the JD *and* the CV *and* the computed gap.

### 7.2 Tier 2 — Aptitude and assessment practice

Not filler. The sampled corpus is heavily banking and finance — Keystone Bank,
First Bank, Dangote, PZ Cussons — and Nigerian banks routinely screen with
SHL-style numerical, verbal and analytical assessments before interview.

So: numeracy, verbal reasoning, analytical, abstract. Timed, with weak-domain
targeting driven by the user's own history. Deterministic scoring, reusing the
same verdict machinery as matching.

### 7.3 Tier 3 — Voice mock interview (deferred)

Speech-to-text, turn-taking, text-to-speech, and a scoring rubric that must be
proven correct on text first. High visible value, large cost, no offline path.
Text-based structured practice ships first: question → answer → rubric feedback.

### 7.4 The guardrail

**Prep must never coach a user to claim experience they do not have.** Not a
compliance nicety — a candidate coached into a lie gets caught in the interview
and loses the job *and* the product. Prep teaches honest framing: transferable
experience, adjacent work, and what they would do differently.

---

## 8. Applications

- Shortlist a role. One application record per **role**, never per posting.
- `Shortlisted → Applied → Interviewing → Closed`
- `Applied` is set **by the user, manually**. The product submits nothing.
- Notes, next-action date, linked prep progress.
- Deferred: offer comparison, interview dossiers, multi-candidate management.

**Auto-submission is not on the roadmap.** The source's apply path is
CAPTCHA-protected; automating it means defeating a CAPTCHA. It is also bad for
the user — bulk applications read as spam and get filtered.

---

## 9. Help and support

Interpretation to confirm: an **in-app support desk** so users can raise a
ticket without leaving the product.

Minimal and deliberate:

```
support_ticket  { user_id, category, subject, status, created_at, resolved_at }
support_message { ticket_id, author_id, body, created_at }
```

Categories: `bug`, `data_issue`, `account`, `feature_request`, `other`.

Why it is in MVP scope rather than deferred: this is an ingest product, and the
single most common user report will be *"this job is wrong"* or *"this job is
missing."* That is a data-quality signal, not just a support burden — it feeds
directly back into adapter health and the probe in `ARCHITECTURE.md §5`.

No SLA, no assignment, no attachments in v1.

---

## 10. Sources

**myjobmag first**, by choice — the adapter interface is built for more and no
part of matching, tailoring, prep, or the UI is aware of which source a role
came from. Adding a board is one file.

Two things the architecture must support from the start:

1. **Capability declaration.** Sources differ in what they provide. myjobmag
   yields contact email ~25% of the time and no logos at all. The UI must read
   declared capability rather than hardcode assumptions about the market.
2. **Honest degradation.** A source behind a bot wall reports degraded and the
   rest of the pipeline continues. Jooble returned 403 to `curl`, full Chrome,
   and Googlebot. When a second source clears that wall, it plugs in — but it
   does not gate anything.

---

## 11. Deferred, with reasons

| Item | Why not now |
|---|---|
| Auto-submission | CAPTCHA-protected. Would require defeating it |
| Second board | Needs proxy spend. Architecture ready, board pending |
| Voice mock interview | §7.3 |
| LinkedIn automation | Not planned. Bans are near-certain; the asset is the user's |
| Compensation filtering | No data exists |
| Employer-facing accounts | Secondary persona |
| Native mobile | Responsive web first; approval is the mobile moment |

---

## 12. What success looks like

1. **Precision** — of 10 roles shown, 6+ are ones the user would genuinely consider.
2. **No duplication** — 0 of the 10 are the same role.
3. **Explainable** — every verdict cites source text.
4. **Honest tailoring** — every claim in a tailored CV traces to a CV span, and the user knows which requirements went uncovered.
5. **Relevant prep** — questions reflect *this* JD and *this* CV's gaps.
6. **Trust** — the user would be comfortable showing a friend the output.
