# JobSuites

[![CI](https://github.com/FehintoluSamuel/JobSuites/actions/workflows/ci.yml/badge.svg)](https://github.com/FehintoluSamuel/JobSuites/actions/workflows/ci.yml)

A job-search suite. Ingests roles from job boards, matches a CV against a job
description, tailors the CV to that role, prepares for its interview, and
tracks the application.

Matching, tailoring, and interview prep are not three features — they are three
questions asked of one shared evidence base. That is the whole architecture.

**Status:** pre-alpha. Ingest, matching, tailoring, and prep are built and
running against live data. Applications tracking is next.

---

## What exists

| Path | What it is |
|---|---|
| `docs/PRODUCT.md` | Scope, matching, tailoring, prep, what's deferred |
| `docs/ARCHITECTURE.md` | Data model, evidence base, pipelines, failure modes |
| `docs/DESIGN.md` | Design system, screens, imagery strategy |
| `docs/RESEARCH.md` | Measured source findings. **Read this first** |
| `ingest/myjobmag_adapter.py` | Working extractor + role clustering |
| `ingest/publish.py` | Probe, crawl per target, publish, report the run |
| `ingest/targeting.py` | Does this job match what the user asked for? |
| `scripts/ingest-scheduled.sh` | One scheduled poll |
| `scripts/install-schedule.sh` | Installs the daily 16:00 launchd agent |
| `design/reference/mockups/` | 8 Stitch mockups + brand assets (visual reference only) |

Verify the source contract still holds before building on it:

```bash
cd ingest && python3 myjobmag_adapter.py
```

---

## Running it

```bash
./scripts/dev.sh        # Postgres, API on :5236, web on :5173
```

Then install the daily ingest so each user's search keeps itself fresh:

```bash
./scripts/install-schedule.sh          # 16:00 daily
launchctl kickstart -k gui/$(id -u)/com.jobsuites.ingest   # run it now
tail -f logs/ingest.log
```

The crawl is per user, not per board. Each scheduled run asks the API which of
your target roles are due, and crawls those — so the roles you set on your
profile are the only ones fetched, and editing them changes tomorrow's run
without touching the schedule. A target's first crawl looks back three months;
later runs are deltas that read only what is new. See `docs/ARCHITECTURE.md`
§5.1a.

Requires an `Ingest:Key` of at least 32 characters; the runner reads it from the
environment, falling back to the API's own config. It is never written into the
launchd plist.

> A launchd job is not Terminal, so macOS denies it `~/Desktop`. From a repo in
> a protected folder the job exits 126 and the board quietly stops updating — the
> installer prints the fix. `scripts/dev.sh` is unaffected.

Poll by hand at any time:

```bash
INGEST_KEY=... python3 -m ingest.publish          # every target that is due
INGEST_KEY=... python3 -m ingest.publish --dry-run
INGEST_KEY=... python3 -m ingest.publish --sweep --limit 5   # board-wide
scripts/ingest-scheduled.sh --dry-run            # same thing via the runner
```

`--targets` bounds how many target roles one run serves (default 8) and
`--limit` how many landing pages each may open (default 25). Two environment
variables on the API side set the policy rather than the crawler:

| Variable | Default | Meaning |
|---|---|---|
| `INGEST_INTERVAL_HOURS` | `24` | How often a target is re-crawled |
| `INGEST_FIRST_CRAWL_DAYS` | `90` | How far back a first crawl looks |

Check what the scheduler would do next, without crawling:

```bash
curl -s -H "X-Ingest-Key: $INGEST_KEY" \
  'http://localhost:5236/api/ingest/queue?limit=20' | python3 -m json.tool
```

Every poll reports itself to `POST /api/ingest/runs`, and
`GET /api/ingest/health` shows the same data an operator sees. The dashboard
shows it too: three successful-but-empty polls in a row, or a failed one, mark
the source degraded rather than leaving a stale list looking like a quiet week.

---

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request:
the API builds and its full suite runs against a real Postgres 18 service
container, the web app lints, builds and tests, and the ingest package is
byte-compiled and imported.

```bash
# what CI runs, locally
dotnet test JobSuites.slnx
(cd web && npm run lint && npm run build && npm test)
python3 -m compileall -q ingest
```

Two details worth knowing before changing it:

- **CI writes its own `appsettings.Development.json`.** The real file is
  gitignored because it holds a machine's database password and signing key, but
  `Program.cs` validates `Jwt:Key` while building the host, before any test-time
  override can run. The workflow writes a throwaway key; it is not a secret and
  no repository secret is needed to run CI.
- **A model change without a migration fails the build.** `dotnet ef migrations
  has-pending-model-changes` runs as its own step, because that mistake compiles
  cleanly and only surfaces at runtime as a schema mismatch.

`.github/workflows/ingest-probe.yml` is separate and manual (`workflow_dispatch`
only). It calls the adapter's `probe()` against the real board, which is the only
check that can detect a template change on the source — but it is a real request
to a third party's site, so it does not belong on every push.

There is no deploy workflow yet, and no scheduled one either: a cron that has no
deployed API behind it fails daily and teaches everyone to ignore the failure.
`docs/ARCHITECTURE.md` §9a records what each piece becomes on Supabase, including
the two repository secrets a hosted scheduler would need — and why the crawler
never needs the database password at all.

## The load-bearing ideas

**One evidence base, three products.** A CV is decomposed once into normalised
facts that each point back at their source text. A JD is decomposed into
requirements. Matching joins them; tailoring selects from them; interview prep
asks about the ones that found no match. Prep costs almost nothing extra once
matching exists.

**Closed-set generation.** Tailoring may select, order, and reframe existing
true facts. It may never assert one. That is a structural bound, not a prompt
instruction. A deterministic post-generation check diffs every entity in the
output against the CV corpus and rejects anything invented. "ChatGPT writes your
CV" is a commodity; "we can prove we didn't invent anything" is a product.

The deterministic engine is always the base. When server-side LLM config is set
(`Llm:BaseUrl`, `Llm:ApiKey`, `Llm:Model` — env or appsettings, never exposed to
the browser), an optional pass refines prose: it is handed only the deterministic
draft and a rewrite that fails the fabrication check is discarded, though it is
kept beside the document so the user can read what was invented. No key
configured means the deterministic engine alone runs, so the app never depends
on an upstream model.

**Roles, not postings, are the unit of work.** Companies post one role once per
state. Measured: 13 postings = 4 real roles, with one role occupying 10 slots of
a 10-item shortlist. Clustering happens at ingest, so the queue cannot flood.

**Evidence, not scores.** Verdicts are categorical and each cites the JD span
and CV facts that produced it. A role that cannot cite evidence is not shown.

**Adapters, not conditionals.** Adding a board is one file. Coverage is
declared per source and measured against reality.

---

## Five facts that shaped this

Full detail in `docs/RESEARCH.md`.

1. **The source has no bot defence.** Plain HTTP, no challenge. This is a normal
   integration, not an arms race.
2. **No structured data.** No JSON-LD, no microdata — CSS-class scraping. Six
   metadata fields are deterministic; the model is only needed for prose.
3. **One role, ten postings.** And the ten job descriptions differ by a few
   characters, so content-hash dedupe catches none of it.
4. **Much of the data the product wanted isn't there.** No employer-stated
   salary, no logos, no company websites. Contact email in ~25% of listings.
5. **Submission cannot be automated.** The apply path is CAPTCHA-protected.
   Auto-submit is not on the roadmap.

Plus one finding worth stating plainly: **the source already ships a free
CV↔JD matcher** on the same corpus. JobSuites competes on role clustering,
explainability, and continuation past the score. See `PRODUCT.md §5.4`.

---

## Principles

- Explain everything, or do not show it.
- Never invent. Surface the gap instead.
- Degrade loudly — a silent zero must be distinguishable from a quiet week.
- Boring infrastructure. No budget, so: Postgres, one API process, one worker.
- Adapters, so a second board is a file and not a rewrite.

---

## Next steps

1. Build the match queue screen against real data and judge the mockups against it.
2. Applications tracking, then a second board adapter — that is where
   "adapters, not conditionals" gets tested.
3. A durable queue, before model-assisted requirement extraction enters the path
   (`ARCHITECTURE.md` §9).
4. Only then: model-assisted extraction, once the retry and backoff it needs
   exist.
