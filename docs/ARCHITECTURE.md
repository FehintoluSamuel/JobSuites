# JobSuites — Architecture

Target: one developer, no budget for managed services. Postgres, one API
process, one ingest process. Optimised for being replaceable, not for scale.

Every decision traces to a measured finding in `RESEARCH.md` or a requirement in
`PRODUCT.md`.

---

## 1. Shape

```
┌──────────────────────────┐      ┌────────────────────────────────┐
│  ingest (Python)         │      │  JobSuites.Api (ASP.NET Core) │
│                          │      │                                │
│  ISourceAdapter          │ POST │  auth · profile · evidence     │
│   ├ MyJobMagAdapter      │─────►│  matching · tailoring          │
│   └ (capability-declared)│ batch│  prep · applications · support │
│                          │      │                                │
│  sitemap poll            │      └──────────┬─────────────────────┘
│  parse · cluster · dedupe│                 │
└──────────────────────────┘           ┌─────▼──────┐
                                        │  Postgres  │
                                        └─────┬──────┘
                                              │
                                    ┌─────────▼──────────┐
                                    │  JobSuites.Worker   │
                                    │  extraction · match │
                                    │  tailor · verify    │
                                    └─────────────────────┘
```

**Python owns acquisition.** It is better at parsing whatever HTML a board
serves, and board formats change without notice. **C# owns everything a user
touches** — auth, evidence, matching, tailoring, documents, support. They meet
at one HTTP boundary.

No message broker initially. Ingest POSTs batches; the API commits each in one
transaction. A durable queue arrives when extraction and tailoring steps need
per-item retry with backoff — not before.

---

## 2. Data model

Four groups: **acquisition**, **evidence**, **application**, **support**.

### 2.1 Acquisition

```
Source ──< Posting ──< RolePosting >── Role ──< Match >── User ──< Application
                │                                            │
                └── raw snapshot (immutable)                 └── Profile
```

Three levels, not two, because one role posted across ten states is the normal
case rather than an edge case.

**`posting`** — one crawled page

```
id, source_id, source_job_id, url
raw_title, company, location_raw, state_norm
job_field, job_type, qualification
experience_raw, min_years, max_years
posted_date, deadline_date
jd_text, jd_hash, contact_emails[]
salary_estimate, company_size_est        -- captured, never filtered on
raw_snapshot_key
first_seen, last_seen, closed_at
unique(source_id, source_job_id)
```

**`role`** — one job, N locations. **The unit of matching.**

```
id, source_id, company_norm, title_norm, canonical_title
first_seen, last_seen, closed_at
unique(source_id, company_norm, title_norm)
```

**`role_posting`** — join, carries the state

```
role_id, posting_id, state_norm
primary key(role_id, posting_id)
```

**`source`**

```
id, name, base_url, adapter_key
status,            -- healthy | degraded | blocked
last_polled_at, last_yield, consecutive_zero
capabilities jsonb -- declared; see §6
```

### 2.2 Evidence — the core

The CV is decomposed once. Matching, tailoring, and prep all read these tables
and nothing else.

**`cv_span`** — verbatim source text

```
id, user_id, section, ordinal, text
evidence_hash          -- detects the CV changing under us
```

**`cv_fact`** — a normalised claim, always pointing at its span

```
id, user_id, span_id
type          skill | employer | title | date_range | education
            | certification | language | domain
value         "PostgreSQL"
normalised    "postgres"
recency_years int null
confidence    numeric
```

`unique(user_id, type, normalised, span_id)`

**`jd_requirement`** — the same shape, per role

```
id, role_id, posting_id
category    skill | experience | education | certification | language | domain
text        "5+ years with SQL and Power BI"   -- verbatim JD span
must_have   bool
years_min   int null
normalised  text[]                            -- synonyms, model-supplied
```

**`fact_alias`** — the synonym table the join runs on

```
normalised  text
category    text
unique(category, normalised)
```

The deterministic join is `fact_alias.normalised` matched against
`jd_requirement.normalised`. Model-extracted synonyms, code-decided fit.

### 2.3 Application

**`match`**

```
id, user_id, role_id
pillars    jsonb    -- [{key, verdict, jd_span, fact_ids[]}]
rationale  text
score_internal numeric   -- ranking only; NEVER rendered as a percentage
computed_at, dismissed_at
unique(user_id, role_id)
```

**`tailored_document`**

```
id, user_id, role_id, version
status         draft | needs_review | approved | rejected
doc_json       jsonb          -- structured output, renderable to DOCX/PDF
diff_json      jsonb          -- what changed vs master, and why
coverage       jsonb          -- must_have requirements: covered / uncovered
verification   jsonb          -- fabrication check result, §5.4
fact_refs      uuid[]         -- every fact the document draws on
approved_at, created_at
```

**`interview_prep`**

```
id, role_id                        -- cached, shared across users
questions  jsonb                   -- generated once per role
created_at

interview_session
id, user_id, role_id, mode         aptitude | technical | general
started_at, finished_at
scores    jsonb                    -- deterministic per-domain verdicts

interview_answer
id, session_id, question_id
body, rubric_feedback jsonb, verdict, honest_framing_used bool
```

**`assessment_item`** — aptitude bank

```
id, domain   -- numerical | verbal | analytical | abstract
difficulty, prompt, options jsonb, answer_key, worked_steps
```

**`application`**

```
id, user_id, role_id
status  Shortlisted | Applied | Interviewing | Closed
applied_at timestamptz null    -- set by the user, manually
notes, next_action_at
unique(user_id, role_id)
```

### 2.4 Support

```
support_ticket  id, user_id, category, subject, status, created_at, resolved_at
                category: bug | data_issue | account | feature_request | other
                status:   open | awaiting_user | resolved | closed
support_message id, ticket_id, author_id, body, created_at
```

Tiny on purpose. No SLA, no assignment, no attachments in v1.

Its real value is not support load — `category = data_issue` is a direct feed
into adapter health. "This job is wrong" and "this job is missing" are the
signals that catch selector drift before `probe()` does.

---

## 3. Why role clustering is load-bearing

Measured: 13 postings from two companies were **4 roles**. Jaza Energy posted
"Field Service Engineer (Solar Energy)" ten times, once per state.

Without clustering, a user matching field service engineering sees **the same
job in all ten slots** of a ten-item shortlist. The product is useless in
exactly the case it matters most.

Both obvious dedupe strategies were tested and both fail, in opposite
directions:

| Strategy | Failure |
|---|---|
| `unique(jd_hash)` | Companies reuse templates — 5 of 20 postings had byte-identical text, so this **collapses 5 distinct roles into 1** |
| `unique(company, title)` | Multi-state postings have distinct titles (`… - Delta`, `… - Abia`), so this **returns 10 rows for 1 role** |

Only title-suffix stripping works:

```
raw_title      "Field Service Engineer (Solar Energy) - Delta"
strip_location → "Field Service Engineer (Solar Energy)"
role_key       (source, company_norm, title_norm)
```

Clustering happens **at ingest**, so the match queue joins `role` and cannot be
flooded regardless of how postings arrive.

The board misspells "Anambara" for Anambra. A literal suffix regex split one
role into two — reproduced during recon, giving 5 roles instead of 4. Matching
is token-based against a canonical state list with an alias map.

---

## 4. Normalisation

**States** — 36 states + FCT, alias map (`anambara → anambra`, `fct → abuja`).
Implemented and verified in `ingest/myjobmag_adapter.py`.

**Experience** — three observed shapes: `"5 years"`, `"1 - 3 years"`,
`"8 - 12 years"`. Parse to `min_years` / `max_years`; open-ended gives
`max_years = NULL`. **50% of postings have none.** Absence means unspecified,
not zero.

**Taxonomy** — `job_field` uses the board's own list, multi-values comma
separated. Users pick target roles **from the same list**. Maintaining an
internal taxonomy costs a mapping table and buys nothing.

---

## 5. The pipeline

### 5.1 Acquisition — Python

```
1. poll   sitemap-main-jobs.xml              45,001 URLs, ~2 MB
2. head   read until first known URL         ← newest-first ordering
3. land   fetch new landing page             → a.subjob-title → /job/{slug}
4. skip   discard known slugs                ← keeps repeat polls near-free
5. fetch  job page, parse to Posting
6. snap   store raw HTML, content-addressed
7. role   resolve or create role; link posting → role with its state
8. push   POST batch to the API
```

**Step 2 is the freshness mechanism.** The sitemap carries no `<lastmod>`, so
recency cannot be diffed by timestamp. It *is* ordered newest-first — verified by
confirming leading entries report today's date. So a poll reads only until it
hits a known URL: typically tens of entries, not 45,001.

**Steps 3–4 bound cost.** A daily poll of new postings only is a few hundred
requests. No proxy needed — the source has no bot defence.

### 5.2 Extraction — C# worker

```
cv upload ──► cv_span[] ──► LLM (structured output) ──► cv_fact[]
posting    ──►               LLM (structured output) ──► jd_requirement[]
                                                  └──► fact_alias[]
```

The model extracts, normalises, and supplies synonyms. **It does not decide
fit.**

### 5.3 Matching — deterministic

```sql
for each requirement r of a role:
    facts = cv_fact f
           join fact_alias a on a.normalised = f.normalised
           where f.user_id = :u and a.category = r.category
             and (r.years_min is null or f.recency_years >= r.years_min)
    verdict = STRONG    if count(facts) >= 2 and any recent
             PARTIAL   if count(facts) = 1
             WEAK      if only adjacent facts
             MISSING   if none
    evidence = (r.text, facts[].span_id)
```

Same inputs, same verdicts, always traceable to source text. No model call on
this path, so it is fast and cheap. **A role with no evidence-bearing verdict is
not shown.**

An optional final LLM call writes prose rationale only. It cannot change a
verdict.

### 5.4 Tailoring — closed-set selection with a deterministic check

```
must_have requirements
      │
      ▼
facts that answer them          ← §5.3 already computed this
      │
      ├─ SELECT    subset of existing facts
      ├─ ORDER     permutation, relevance-leading
      └─ REFRAME   wording, constrained to source facts
      │
      ▼
FABRICATION CHECK              ← deterministic, not a model judgement
      │  extract every entity from the output (companies, numbers,
      │  dates, technology nouns); diff against the CV corpus
      ▼
   clean?  ──no──► REJECT + show the user exactly what appeared
      │ yes
      ▼
GAP SURFACE  requirements with no supporting fact, shown not invented
      │
      ▼
USER REVIEW   side-by-side · diff · coverage · traceable change reasons
      │
      ▼
APPROVE → export DOCX / PDF
```

The model **never gains the ability to assert a fact**. It selects from a
closed set of `cv_fact` ids. This is an architectural guarantee, not a prompt
instruction — a prompt is a request, a closed set is a bound.

The fabrication check is plain entity extraction and set membership over the
fact table. No model call, so it is trustworthy and free.

Nothing is dispatched automatically at any point.

### 5.5 Prep — amortised

Prep content is generated **per role and cached**, then personalised per user by
gap. The expensive part is paid once and served to every user whose gaps match.
This is what keeps it affordable at zero budget.

```
jd_requirement ──► gap = requirements with no supporting cv_fact
                        │
                        ▼
              prep questions for role (cached on interview_prep)
                        │
                        ▼
              layer user's own facts and honest framing
```

Question generation traces each question to a `jd_requirement`, and each
suggested answer traces to a `cv_fact`. **No path produces a claim the user
cannot source.** If a gap has no supporting fact, the answer frame is adjacent
experience or learning narrative — never invented experience.

Aptitude scoring reuses §5.3 verdict machinery: deterministic, per-domain.

---

## 6. Source abstraction

```python
class SourceCapabilities:      # declared, drives the UI
    fields: set[str]           # salary | contact_email | logo | company_site
                             # | posted_date | deadline | experience | …
    freshness: Literal["lastmod", "ordered", "unknown"]
    requires_proxy: bool
    max_depth: int | None

class ISourceAdapter(Protocol):
    capabilities: SourceCapabilities
    def discover(self) -> Iterator[SourceRef]: ...   # URL + optional lastmod
    def fetch(self, ref: SourceRef) -> RawDocument: ...
    def parse(self, doc: RawDocument) -> list[Posting]: ...
    def probe(self) -> SourceHealth: ...
```

**Capability declaration exists because coverage is not uniform.** myjobmag
yields contact email ~25% of the time and no logos at all. The UI reads
`capabilities.fields` rather than hardcoding what the market provides, so a
second source with different coverage changes nothing in the frontend.

Declared capabilities are a claim. `source_field_coverage` is a **measured**
view computed from actual postings — declared and measured are compared, and
drift raises a warning.

### Adding a board

One adapter file plus a `source` row. Matching, tailoring, prep, the API, and
the UI are untouched.

### Degradation

A blocked source raises `BlockedByBotDefense`, the run is recorded `degraded`,
and every other source continues. **One blocked board must never halt ingest.**
Jooble returned 403 to `curl`, full Chrome, and Googlebot — when it clears that
wall it plugs in, and until then nothing else waits for it.

### `probe()` — the anti-silent-failure guard

Asserts a known-good URL still parses and still yields the expected fields, on a
schedule. A template change on the source yields **zero rows with no HTTP error
and no exception**. That failure mode is indistinguishable from a quiet market
unless something is actively checking. This is the single highest-value piece of
defensive code in the ingest path.

---

## 7. Health and observability

Every ingest writes an `ingest_run`:

```
source_id, started_at, finished_at
postings_seen, postings_new, roles_new
status, error
```

The UI surfaces `last_polled_at` with a staleness threshold, yield per run, and
`degraded` when yield is zero for N consecutive runs.

Zero-yield is a **dashboard warning**, never a silent condition. A queue that
emptied because a selector broke must be distinguishable from a quiet week.

---

## 8. API surface

```
POST   /api/internal/ingest                source-key auth, batch commit
GET    /api/roles                          clustered, never raw postings
GET    /api/roles/{id}                     + pillars + evidence
POST   /api/matches/recompute
GET    /api/matches                        the shortlist
POST   /api/matches/{id}/dismiss

POST   /api/profile/cv                     upload → spans + facts
PATCH  /api/profile/facts/{id}             user corrections
POST   /api/tailoring                      role_id → draft
GET    /api/tailoring/{id}                 diff + coverage + verification
POST   /api/tailoring/{id}/approve
GET    /api/tailoring/{id}/export.pdf

GET    /api/prep/{role_id}                 cached questions
POST   /api/prep/sessions                 start aptitude | technical
POST    /api/prep/sessions/{id}/answers

GET    /api/applications · POST · PATCH
POST   /api/support/tickets · POST /api/support/tickets/{id}/messages
GET    /api/health                          per-source status
```

`/api/internal/*` is key-authenticated and never exposed to a browser. Every
other endpoint is scoped by `user_id` **in the query, not in application code** —
one missing predicate is a cross-user data leak, and CV text is involved.

---

## 9. Scheduling

MVP: ingest on an OS timer. After a successful commit it POSTs
`/api/matches/recompute`.

Deferred deliberately: **a durable queue.** It becomes necessary the moment
extraction or tailoring enters the path, because those steps can fail, cost
money, and need per-item retry with backoff. At that point use a Postgres-backed
queue rather than bolting retries onto a timer loop.

Known trap for whenever it lands: .NET `BackgroundService` and host lifecycle can
**silently drop queued work** on restart or redeploy. Anything that must not be
lost belongs in a persisted queue with explicit status, never in memory.

---

## 10. Deferred architecture

Seams exist; the complexity does not.

| Concern | Seam | Trigger to build |
|---|---|---|
| More boards | `ISourceAdapter` + capabilities | a second board clears its bot wall |
| Gated contact data | host-scoped browser extension | user requests contact lookup |
| Durable jobs | §9 | first LLM step in the request path |
| Voice interviews | `interview_session` mode | text rubric proven |
| Employer-facing | `user_id` present throughout | employer product |

**Extension boundary.** The board gates contact details behind login. The
product will not store user passwords. A host-scoped extension — where the user
logs in themselves in their own browser and the extension reads only on explicit
action — retrieves the same data with zero credential custody. Server-side
ingest stays anonymous and credentials-free.

---

## 11. Failure modes

| Failure | Response |
|---|---|
| Selector drift | `probe()` fails → source `degraded`, dashboard warning, ingest continues |
| Source 403 / Cloudflare | `BlockedByBotDefense` → `degraded`, other sources continue |
| Partial batch failure | One transaction per batch; raw snapshots retained for replay |
| Malformed JD | Posting stored, role created, matched on taxonomy alone, flagged `low_confidence` |
| Duplicate reappears | `role_posting` upsert, `last_seen` refreshed, no duplicate role |
| Role disappears | `closed_at` set, hidden from queue, history retained |
| Ingest overlaps | Postgres advisory lock on `source_id`; second run exits |
| Tailoring fabricates an entity | Verification fails → document rejected, offending entity shown to the user |
| Requirement unmatched | Surfaced as uncovered. Never invented |
| LLM extraction fails | Facts stay unpopulated; role matches on taxonomy alone and is flagged |
