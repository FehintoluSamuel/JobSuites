# JobSuites

A job-search suite. Ingests roles from job boards, matches a CV against a job
description, tailors the CV to that role, prepares for its interview, and
tracks the application.

Matching, tailoring, and interview prep are not three features — they are three
questions asked of one shared evidence base. That is the whole architecture.

**Status:** pre-alpha. Ingest proven against live data. No application code yet.

---

## What exists

| Path | What it is |
|---|---|
| `docs/PRODUCT.md` | Scope, matching, tailoring, prep, what's deferred |
| `docs/ARCHITECTURE.md` | Data model, evidence base, pipelines, failure modes |
| `docs/DESIGN.md` | Design system, screens, imagery strategy |
| `docs/RESEARCH.md` | Measured source findings. **Read this first** |
| `ingest/myjobmag_adapter.py` | Working extractor + role clustering. The only executable code |
| `design/reference/mockups/` | 8 Stitch mockups + brand assets (visual reference only) |

Verify the source contract still holds before building on it:

```bash
cd ingest && python3 myjobmag_adapter.py
```

---

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

1. Stand up `JobSuites.Api` (C#) and have the adapter POST through its endpoint.
2. Build the evidence base: CV upload → spans → facts, with user correction.
3. Extract `jd_requirement` per role and build the deterministic match join.
4. Build the match queue screen against real data and judge the mockups against it.
5. Only then: tailoring, then prep. Both read the evidence base and are largely
   consequence rather than new ground.
