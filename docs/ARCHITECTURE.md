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

**`crawl_target`** — what one user is looking for, and the crawl that serves it.
Added when acquisition moved from a board-wide sweep to per-user retrieval; see
§5.1a.

```
id, user_id, title, states[], max_jobs_per_run
last_crawled_at, cursor, consecutive_empty_runs
unique(user_id, title)
```

`roles` stays shared across users even though crawling is per-user. A role found
for one user's search can match another's profile, and only the match layer knows
that; the crawl decides what to *fetch*, not what to *store*.

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

### 5.1a. Per-user retrieval (targeted crawl)

The sweep above answers "what is on the board". It does not answer "what is new
*for this user today*", which is the question the product asks. Retrieval is
therefore driven by `crawl_target` rows rather than by the sitemap alone.

```
1. queue GET /api/ingest/queue          → target roles that are due
2. known GET /api/ingest/known-ids      → job slugs already stored
3. scan  sitemap positions [cursor, +window)   ← one request, no per-landing cost
4. rank  strong landings first, weak as fallback
5. open  at most max_landings of them
6. skip  known slugs, off-target titles, out-of-window dates, other states
7. push  POST batch, then report the run with the new cursor
```

**The API decides what to crawl; the script has no user list.** That is what lets
a user edit their target roles and have tomorrow's scheduled run honour it with
no change to the schedule or to the crawler.

**Relevance is applied locally, in three cheap stages, because the source offers
nothing better.** `robots.txt` disallows `/search/jobs?*` and every `?`-bearing
path, so there is no queryable endpoint to push a title into. Instead:

1. *Landing slug* (`/jobs/software-engineers-at-acme`) names the occupation, and
   is checked **before** the landing is fetched.
2. *Job title* is checked before the detail page is fetched, from the job path's
   own slug as a cheap pre-filter, then authoritatively on the parsed `h1`.
3. *Recency* is checked last, because a posting date only exists on the detail
   page.

Scanning the sitemap is one request; opening a landing is not. So the window of
sitemap positions considered (default 400) is deliberately far larger than the
number of landings opened (default 25).

**Strong before weak.** A landing sharing one occupation word with the target is
weak evidence — "engineer" appears in `sales-engineer`, `well-engineer` and
`cost-engineer`, so a software target would otherwise open hundreds of
oil-and-gas pages to discard them. All-words matches are taken first, and
single-word landings are only opened if the budget is still unfilled.

**A target is a conjunction, not a vote.** `job_title_relevant` requires *every*
requested occupation word to be present, after dropping seniority words and
canonicalising synonyms (`engineering` → `engineer`). A "match most of the
words" rule was tried and rejected: it reported a Civil Engineer under a
"Mechanical Engineering" target because the single shared word was half the
request. Precision is worth more than recall here — one wrong role in the feed
costs the user more than a missing one, and an adjacent search is a second target
role, not a looser filter.

**First crawl is a bounded search; later runs are deltas.** A target's first run
applies a recency window (default 90 days, `INGEST_FIRST_CRAWL_DAYS`) and its
cursor walks forward through the sitemap position by position, so successive runs
cover more history rather than re-reading the head. A *daily* run does the
opposite: it starts at the head, because that is where new postings appear, and
relies on the known-ID set to skip what it has already seen. Skipping is a local
slug comparison, not a request, so re-reading the head is close to free.

**A target that finds nothing is not a broken board.** Source health is judged
only from board-wide sweeps. Counting targeted runs toward the source's
zero-yield counter made the dashboard declare MyJobMag degraded after three
quiet searches — the probe had passed and the crawler had been busy; the user's
chosen vocabulary was simply not on the board that day. Per-target quietness
lives on `crawl_target.consecutive_empty_runs`.

**Cost scales with users, not with the board.** Each target is a separate walk,
so N users means N crawls. The politeness delay and the per-target landing
budget are what keep that affordable, and they are also the reason this does not
scale to a large user base without a different design (§9).

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

**As built.** `source` holds the running health of one board (status, last poll,
last yield, consecutive zero-yield runs); `ingest_run` holds one poll. Three
consecutive successful-but-empty polls mark the source `degraded` — a *failed*
poll does not count toward that threshold, because a failure is already visible
and a quiet-but-successful poll is the one that hides.

Three decisions worth keeping:

- **Probe before crawl.** A template change on the source yields zero rows with
  no HTTP error and no exception. `ingest.myjobmag_adapter.probe()` parses one
  known page first; if that fails the run is recorded as degraded and the crawl
  is skipped, rather than spending the poll's requests to rediscover it.
- **Every run reports, including empty ones.** Reporting only on success makes
  the zero-yield signal undetectable, which is the whole point of the row.
- **Concurrent runs stand down.** The write path takes a Postgres advisory lock
  keyed on the source (`pg_try_advisory_xact_lock(hashtext('jobsuites:ingest:{source}'))`)
  and the loser returns 409 having written nothing. The lock is transactional, so
  it releases on commit or crash without a cleanup step.

```sql
-- one row per source, upserted by key
source(key, name, status, last_polled_at, last_seen_at,
       last_yield, consecutive_zero_runs)

-- one row per poll
ingest_run(source_id, started_at, finished_at, status,
           postings_seen, roles_published, probe_detail, error)
```

`GET /api/ingest/health` and the dashboard's `ingest` block both read from these
tables, so what the user sees and what an operator sees cannot disagree.

---

## 7a. Requirement extraction

`jd_requirement` is the join target for matching, tailoring and prep, and it is
extracted **once, at ingest**, from the JD body — not per request and not from
the board's taxonomy alone. `role_skills` is whatever myjobmag structured for
us; the prose is where the actual ask lives, and it is frequently the only place.

The extractor (`Matching/RoleRequirements.cs`) is deterministic. No model, no
network, no per-user cost — the same posting always yields the same requirement
set, which is what lets a stored `tailored_document` still mean what it meant
when it was written.

Two rules do the work:

1. **Must-have is read from the JD's own wording**, not inferred from position
   or count. A requirement is disqualifying if the description says so
   ("must have", "required", "essential") or files it under a requirements
   heading without downgrading it. Weak qualifiers ("familiarity with",
   "exposure to") downgrade to *wanted* even inside a requirements block: the
   error is resolved towards understating a gap, because a gap the candidate
   does not actually have is what teaches users to ignore the gap list.
2. **Nothing is emitted without evidence.** Every row carries the JD line it was
   read from (`span`). "Uncovered" is a claim about a real person, so it has to
   be checkable against the posting.

`category` is nullable on purpose. A posting that says "must have hands-on
experience with DCS and SCADA" is making a demand; labelling it `skill` because
that is probably right would invent a requirement the employer never wrote.

A role's rows are replaced when its `jd_hash` changes and kept when it does
not, so the cost is paid once per JD revision rather than per user per request.
Consumers must `.Include(r => r.Requirements)` — without it `Extract` silently
falls back to the taxonomy and the posting's actual asks are ignored.

**Deferred deliberately:** model-assisted extraction. §9 explains why — it can
fail, costs money, and needs per-item retry, so it belongs behind a durable
queue, not in a request path. `jd_requirement.origin` already carries
`"deterministic"`/`"llm"` so an assisted requirement can always be traced to the
engine that asserted it.

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

## 8a. Continuous integration

`ci.yml` builds and tests on every push and pull request. Three jobs, no matrix:
the API suite needs a real Postgres and takes longer than the other two, so
running it alongside them is the whole reason for splitting rather than a
formality.

What CI deliberately does not do:

- **No live crawl.** `probe()` is the only thing that can detect a template
  change on the source, and it is a real request to a third party. It lives in
  its own manually-dispatched workflow (`ingest-probe.yml`) because re-confirming
  the board on every push spends someone else's patience for no new information.
- **No LLM key.** `Llm:Enabled` is false without one, so CI exercises the
  deterministic extraction and matching paths. That is the more valuable path to
  keep honest, since it is the one that runs when the upstream is unavailable.
- **No path filters.** Skipping a job because only documentation changed is how a
  required status check silently stops running.

The suite creates a throwaway database per run from `JOBSUITES_TEST_CONNECTION`
and drops it afterwards, so the CI Postgres needs no schema setup of its own.

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

**As built.** `launchd`, not a `BackgroundService`, per the trap above: an
in-process timer stops when the API is not running, which is exactly when a
laptop is closed, and a missed poll is a silently stale board.

```
scripts/ingest-scheduled.sh          one poll, runnable by hand
scripts/install-schedule.sh          writes and loads the agent (16:00 default)
scripts/uninstall-schedule.sh
```

`RunAtLoad` is on because `StartCalendarInterval` on a sleeping Mac fires at
next wake; a missed poll should run at next login, not be skipped until
tomorrow. The runner reports a failure to reach the API rather than skipping
quietly, so a schedule that cannot do its job is visible on the dashboard.

`Ingest:Key` is read at run time from the environment, falling back to the API's
own config, and is never written into the plist — `launchctl print` shows a
job's environment to any process on the machine.

**Environment note:** a launchd job is not Terminal, so macOS denies it
`~/Desktop`, `~/Documents` and `~/Downloads`. From a repo inside one of those
folders the job exits 126 and the board quietly stops updating. The installer
detects this and prints the fix (grant `/bin/bash` Full Disk Access, or move the
repo). `scripts/dev.sh` is unaffected because a human started it.

### 9a. When this moves off the Mac

Recorded now so the deploy is a checklist rather than an investigation. Nothing
here is built yet, deliberately: a scheduled workflow with no deployed API behind
it would fail once a day and train everyone to ignore the failure signal.

| Piece | Becomes | Note |
|---|---|---|
| Postgres (brew, :5433) | Supabase | connection string becomes a secret |
| `scripts/dev.sh` | container or PaaS service | needs `ASPNETCORE_ENVIRONMENT=Production` |
| launchd agent | a `schedule:` workflow in Actions | same script, different trigger |
| local `appsettings.Development.json` | injected config | never committed, in any environment |

**The scheduler does not need database access.** This is the useful property of
the current split: the crawler speaks HTTP to the API and the API owns the
database, so a GitHub Actions schedule needs exactly two repository secrets —
`JOBSUITES_API` and `INGEST_KEY`. The Supabase password never has to reach the
runner that does the crawling.

`Program.cs` already refuses to start outside Development without an
`Ingest:Key` of at least 32 characters, so a misconfigured deploy fails loudly
instead of exposing write access to job data with the development fallback.

**Supabase specifics worth knowing before the first deploy.** The direct
connection string is IPv6-only, and GitHub-hosted runners are IPv4 — a scheduled
workflow will fail to connect with a direct string and no obvious cause. Use the
Supavisor pooler string, and keep `SSL Mode=Require`. A pooled connection is also
the right choice for the API, which opens several contexts per request.

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
