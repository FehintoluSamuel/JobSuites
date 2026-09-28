# JobSuites — Source Reconnaissance

Empirical findings gathered directly from the two target sources. Everything here
is measured, not assumed. This is the input to the requirements / design /
architecture rewrites.

- Date: 2026-09-28
- Method: direct HTTP fetches, `robots.txt` + sitemap inspection, 38 job/landing
  pages sampled at ~1 req/sec with a real browser UA
- Scope: `myjobmag.com`, `ng.jooble.org`

---

## 1. Source A — myjobmag.com

### 1.1 Feasibility: EASY. No bot defence.

| Probe | Result |
|---|---|
| Job detail page | `200`, ~79 KB, no challenge |
| Landing page | `200`, ~81 KB, no challenge |
| 18/18 sampled landing pages | all `200`, zero blocks |
| Full browser UA required | No — minimal `User-Agent` header suffices |
| JSON-LD / microdata | **None.** No `JobPosting` schema, no `itemprop` anywhere |

Parsing is pure CSS-class scraping. This is a conventional server-rendered PHP
application. There is no anti-bot layer to defeat.

### 1.2 robots.txt — the constraint that defines the architecture

```
User-agent: *
Disallow: /search/jobs?*
Disallow: /*?              ← every URL with a query string
Disallow: /user/*
Disallow: /apply-now/
Disallow: /job-application/
Sitemap: https://www.myjobmag.com/sitemapindex.xml
```

`Disallow: /*?` blocks the entire search and pagination surface. `form action="/search/jobs"`
confirms that is the search endpoint. **Do not crawl search pages.**

The site publishes 7 sitemaps. `sitemap-main-jobs.xml` holds **45,001 URLs**.
This is the sanctioned ingestion path and it is better engineering anyway:
no pagination logic, no query strings, tiny request count.

### 1.3 Sitemaps available

```
sitemapindex.xml
├── sitemap.xml
├── sitemap-2.xml
├── sitemap-main-jobs.xml      45,001 URLs   ← primary source
├── sitemap-sub-jobs.xml
├── sitemap-jobtitle.xml
├── sitemap-company.xml
└── sitemap-custom-pages.xml
```

- **Jooble-style `<lastmod>` is absent from `sitemap-main-jobs.xml`** — verified
  with `lastmod=0` across all 45,001 entries. You cannot use timestamp diffing.
- **Sitemap order is approximately newest-first.** Verified: the first four
  entries all report `Posted: Sep 28, 2026` (today). This enables a cheap
  freshness check — fetch only the first N entries and stop at the first known URL.

### 1.4 Discovery is TWO-HOP

The sitemap contains **company/aggregate** pages, not individual jobs.

```
/jobs/{slug}                       ← in sitemap  ("Jobs at PZ Cussons")
        └── <a class="subjob-title" href="/job/{slug}">   ← individual job
                /job/{slug}        ← the real job detail page (~79 KB, full JD)
```

Measured: 18 landing pages yielded **81 individual job URLs**, mean **4.5 jobs per
landing page** (range 2–30).

> Note: the observed range 2–30 is noisy because the sample includes small
> company pages. Establish the true distribution during the pilot, not from n=18.

### 1.5 Verified selector map

Confirmed present on every page sampled (n=20 individual job pages).

| Field | Selector | Coverage |
|---|---|---|
| Title | `h1` (text = `{role} at {company}`) | 20/20 |
| Job Type | `span.jkey-title`="Job Type" → `span.jkey-info` | **20/20** |
| Qualification | same pair, "Qualification" | **20/20** |
| Experience | same pair, "Experience" | 10/20 (50%) |
| Location | same pair, "Location" | **20/20** |
| City | same pair, "City" | 4/20 (20%) |
| Job Field | same pair, "Job Field" | 19/20 (95%) |
| Posted date | `div#posted-date` | **20/20** |
| Deadline | `div.read-date-sec-li` (2nd sibling) | 20/20 |
| Salary estimate | `div.salary-widget-amount` | 6/20 (30%) |
| Company size | inside `.salary-widget-amount` ("from N employees") | 6/20 |
| JD body | `div.job-details` | **20/20, zero empty** |
| Employer logo | — | **0/20 — does not exist** |
| Employer website | — | **0/20 — does not exist** |
| Contact email | regex over JD body | **5/20 (25%)** |

`span.jkey-title` / `span.jkey-info` is a clean key-value pair structure. Six
metadata fields come free and deterministic — **the LLM is only needed for the
JD body, not for field extraction.** This is a major cost and error-rate saving
versus the spec as originally written.

### 1.6 JD body quality

```
length: min 2,059  ·  median 2,746  ·  max 5,798 chars
empty:  0/20
```

Consistent, well-structured, English-language prose (≈340–800 words). Excellent
LLM input. Sanity-checked sample is a genuine, detailed role description — not
keyword soup.

### 1.7 Three findings that break naive assumptions

**(a) Salary is company-level, not role-level.**
Six sampled roles returned the identical value `₦ 168K from 38 employees`.
It is a myjobmag estimate derived from company size, not employer-stated
compensation. Only 30% of jobs carry it at all. **Never treat it as a comp
filter input.** Your PRD's "compensation floor $195k" has no data behind it —
see §1.8.

**(b) Companies post template JDs — content-hash dedupe will over-merge.**
Of 20 sampled jobs, **5 shared a byte-identical normalised JD body**. A naive
`sha256(jd_body)` unique constraint collapses 5 distinct open roles into 1.
Dedupe must be keyed on `(source, company, title)` with the hash as a
*similarity hint*, never as the primary key.

**(b2) One multi-state role can consume the entire match queue.**
The single most important finding in this recon. A targeted crawl of two
companies returned **13 postings which represent only 4 distinct roles**:

```
Jaza Energy — "Field Service Engineer (Solar Energy)"
  Delta, Abia, Kogi, Ekiti, Ogun, Kaduna, Osun, Ondo, Edo, Anambara
  = 10 postings, 1 role
```

Three consequences:

1. **A "Top 10 matches" queue can be filled entirely by a single job.** For any
   user whose profile matches solar/field-service engineering, 10 of 10 slots
   would be one role at one company. The headline feature is unusable without
   role-level clustering.
2. **Content hashing does not catch it — the JD bodies are *not* identical.**
   All 10 differ by 10–40 characters, because each names a different state
   (lengths 4,833–4,877; **10 distinct hashes across 10 postings**). Hash-based
   dedupe returns zero matches here. Title-based clustering is the only thing
   that works.
3. **The state names are misspelled.** myjobmag publishes "Anambara"; the correct
   spelling is "Anambra". This was reproduced during recon — a literal
   location-suffix regex split this role into two clusters, giving 5 roles
   instead of 4. Clustering must map against a canonical state list with fuzzy
   token matching, not literal strings. Fixed in
   `ingest/myjobmag_adapter.py::canonical_state`.

Required data model, therefore three levels, not two:

| Level | Identity | Purpose |
|---|---|---|
| Posting | `(source, company, exact title)` | one crawled page |
| Role | `(source, company, title − location suffix)` | one job, N locations |
| Application | user ↔ role | the match-queue unit |

The match queue must operate on **Role**, never on Posting.

**(c) There are no external apply URLs.**
0/20 JD bodies contained a third-party URL. Application is **always** through
myjobmag's own on-page form, which collects Name, Email, Phone, Location and
posts to `/apply-now/{id}`. There is no Greenhouse/Lever/Workable URL to target.
Automated submission therefore means automating myjobmag's form endpoint —
which is both robots-disallowed (`/apply-now/`) and exactly the endpoint that
would attract spam controls.

### 1.8 Implications for the original requirements

| Original PRD promise | Reality on this source |
|---|---|
| "HR/recruiter email where available" | Available in **25%** of JDs. Enrichment required for the rest. |
| "Salary thresholds" / comp floor | **No employer-stated salary anywhere.** Only a 30%-coverage estimate. |
| "Company information" | Company name + sector + size only. **No website, no logo.** |
| "Cross-portal deduplication" | Must be key-based; content hashing is actively harmful (§1.7b). |

### 1.9 Competitive note — material

**myjobmag already ships `/cv-match` — "CV Job Description Matcher — Match your
CV to a Job Description".** It is a first-party CV↔JD matcher on the same
corpus you plan to scrape, offered free to its users.

This is not a footnote. Any matching work needs a clear answer to: *why is
JobSuites's match better than the one the source already gives away for free?*
Address it in the requirements before building the matcher.

---

## 2. Source B — ng.jooble.org

### 2.1 Feasibility: HARD BLOCKED

| Client | Status | Body |
|---|---|---|
| `curl/8.7.1` | **403** | "Just a moment..." (Cloudflare) |
| Full Chrome 131 UA | **403** | "Just a moment..." (5.4 KB) |
| `Googlebot/2.1` | **403** | **"Attention Required! \| Cloudflare"** |

The Googlebot result is the significant one. `robots.txt` explicitly welcomes
`GoogleBot`, yet the WAF still returns a hard block page — so the WAF rule is
stricter than the site's own robots.txt, and it is not a soft JS challenge but
an outright deny.

ng.jooble.org is not an independent small board. Jooble.org is an international
aggregator network running Cloudflare bot management across its instances.

### 2.2 What unblocking would require

Roughly, in order of escalation:

1. Residential/datacentre proxy rotation — mandatory; single IPs burn fast
2. Headless browser with real Chrome (Playwright)
3. **TLS + HTTP/2 fingerprint impersonation** — plain Playwright is detected;
   this needs `curl_cffi` / Camoufox / undetected-chromedriver class tooling
4. Persistent browser profile + cookie jar to clear the interstitial
5. Per-host rate limiting with backoff on 403/429

This is a sustained maintenance commitment and an ongoing proxy spend. It is
an arms race that gets harder over time, not easier.

### 2.3 Available regardless

`robots.txt` offers `sitemap.xml` → `sitemap_tree_kwserp_en_NG_1..N.xml`, and
these **do** carry `<lastmod>` (verified `2026-09-27T22:07:06+00:00`). So URL
*discovery* is cheap even when detail-page *fetching* is blocked. That
degradation mode should be modelled explicitly: discover → attempt → report
`blocked_by_bot_defense` → degrade, don't crash the pipeline.

### 2.4 Recommendation

Sequence Source B last, behind a working Source A, and behind a proxy budget
decision. Do not let it block the product.

---

## 3. Cross-cutting conclusions

### 3.1 Ingest model this data supports

Sitemap-diff, not search-crawl:

```
poll sitemap head (newest-first) → stop at first known URL
  → for new landing URLs: fetch, extract a.subjob-title → /job/{path}
    → fetch job page → extract .job-key-info + .job-details
      → dedupe on (source, company, title) + content hash as a hint
        → store, then match
```

Request budget: ~45k landing pages total; a daily full sweep of new-URLs-only is
a few hundred requests. Well within polite limits.

### 3.2 Imagery — confirmed unobtainable from source

Both sources yield **no employer logos and no people photos**. myjobmag has
exactly one `<img>` on a job page and it is *myjobmag's own logo*; the
`og:image` is a site-wide generic JPEG. The sample company — PZ Cussons — has
no linked website in the page.

Consequences:
- Company identity must be derived from the employer's own domain, which the
  crawl does not supply. Requires a separate discovery step, and will miss
  entirely for employers with no website — a large share of this market.
- Recruiter/people photography has **no reliable automated source**. This is
  structural, not an engineering-effort problem.
- Plan on user-supplied imagery plus typeset wordmark fallbacks, not on a
  harvested photo pipeline.

### 3.3 Blocking issues to resolve before writing requirements

1. **Jooble** — is there a proxy budget? If not, drop Source B and document
   Source A only. Do not build speculative anti-bot infrastructure.
2. **Compensation** — no employer-stated salary exists on myjobmag. Either
   drop comp filtering as a feature or source it elsewhere (company websites,
   enrichment APIs). This invalidates a stated PRD requirement.
3. **Matching differentiation** — `/cv-match` exists on the source. Define why
   yours differs.
4. **Recruiter email** — 25% native coverage. Decide enrichment strategy or
   reduce the promise.
5. **Submission model** — no external apply URLs; everything routes through
   myjobmag's own form. Confirm the intended submission flow before any
   architecture doc describes it.
