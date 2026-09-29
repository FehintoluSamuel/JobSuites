"""
myjobmag adapter — verified against live pages 2026-09-28.

Recon notes (see ../docs/RESEARCH.md §1):
  - No bot defence. Minimal UA works. ~79KB server-rendered PHP.
  - No JSON-LD, no microdata. CSS-class scraping only.
  - robots.txt disallows /search/jobs?* and /*?  -> use the sitemap, never search.
  - sitemap-main-jobs.xml = 45,001 landing URLs, no <lastmod>, newest-first.
  - Discovery is two-hop: /jobs/{slug} -> a.subjob-title -> /job/{slug}
  - Six metadata fields are deterministic via span.jkey-title/jkey-info.
    The LLM is only needed for the JD body.
  - Salary widget is COMPANY-level and absent ~70% of the time. Do not filter on it.
  - ~25% of postings share a byte-identical JD body (company templates).
  - One role is posted once per state; 13 postings can be 5 roles. Content
    hashing cannot detect this. Cluster on title-with-location-stripped.
"""

from __future__ import annotations

import gzip
import hashlib
import html
import re
import time
import urllib.request
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import date, datetime
from typing import Iterator

from .targeting import landing_slug, tokenise

BASE = "https://www.myjobmag.com"
UA = (
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
)

# Fields present as jkey-title / jkey-info pairs. Coverage measured over n=20.
JKEY_FIELDS = {
    "job_type": ("Job Type", 1.00),
    "qualification": ("Qualification", 1.00),
    "experience": ("Experience", 0.50),
    "location": ("Location", 1.00),
    "city": ("City", 0.20),
    "job_field": ("Job Field", 0.95),
}

_TAGS = re.compile(r"<(script|style)\b.*?</\1>", re.S | re.I)
_BLOCK = re.compile(r"</(?:p|li|div|h[1-6]|tr|section)>", re.I)
_BR = re.compile(r"<br\s*/?>", re.I)
_TAG = re.compile(r"<[^>]+>")
_WS = re.compile(r"[ \t]+")
_EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")


class BlockedByBotDefense(RuntimeError):
    """Source is actively blocking automated access. Degrade, do not crash."""


def _text(fragment: str) -> str:
    s = _TAGS.sub(" ", fragment)
    s = _BR.sub("\n", s)
    s = _BLOCK.sub("\n", s)
    s = _TAG.sub(" ", s)
    # Entities are decoded last, after tags are gone: a JD says "Experience &
    # Qualifications", and a requirement row reading "Experience &amp;
    # Qualifications" shows the user the markup instead of the heading. The
    # span is meant to be the posting's words as a reader would see them.
    s = html.unescape(s)
    s = _WS.sub(" ", s)
    return "\n".join(line.strip() for line in s.split("\n") if line.strip())


def _fetch(url: str, *, timeout: int = 30, retries: int = 2) -> str:
    """Plain GET. No proxy, no headless browser — unnecessary for this source."""
    last: Exception | None = None
    for attempt in range(retries + 1):
        try:
            req = urllib.request.Request(
                url, headers={"User-Agent": UA, "Accept-Encoding": "gzip"}
            )
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                raw = resp.read()
                if resp.headers.get("Content-Encoding") == "gzip":
                    raw = gzip.decompress(raw)
                text = raw.decode("utf-8", "replace")
        except urllib.error.HTTPError as exc:
            if exc.code in (403, 429):
                raise BlockedByBotDefense(f"{url} -> HTTP {exc.code}") from exc
            last = exc
        except Exception as exc:  # noqa: BLE001
            last = exc
        else:
            if "Just a moment" in text[:4000]:
                raise BlockedByBotDefense(f"{url} -> interstitial challenge")
            return text
        time.sleep(2**attempt)
    raise RuntimeError(f"fetch failed: {url}") from last


@dataclass(slots=True)
class JobPosting:
    """One crawled page. Not the unit of matching — see role_key()."""

    source: str
    source_job_id: str
    url: str
    title: str
    company: str
    location: str = ""
    city: str = ""
    state_norm: str | None = None
    job_field: str = ""
    job_type: str = ""
    qualification: str = ""
    experience_raw: str = ""
    min_years: int | None = None
    max_years: int | None = None
    posted: date | None = None
    deadline: date | None = None
    jd_body: str = ""
    contact_emails: list[str] = field(default_factory=list)
    company_size_estimate: int | None = None
    salary_estimate: str = ""
    jd_hash: str = ""
    raw_html_key: str = ""

    @property
    def dedupe_key(self) -> tuple[str, str, str]:
        """Posting identity. Content hash is deliberately NOT the key — ~25% of
        postings share a byte-identical JD body. See RESEARCH.md §1.7b."""
        return (
            self.source,
            self.company.lower().strip(),
            self.title.lower().strip(),
        )


# --- Role clustering -------------------------------------------------------
# One role posted once per state => 10 postings, 1 job. The 10 JDs differ by
# 10-40 chars (each names its own state), so hashing catches none of it.
# Title-suffix stripping is the only reliable signal. RESEARCH.md §1.7b2.

NIGERIAN_STATES = (
    "Abia|Abuja|FCT|Adamawa|Akwa Ibom|Anambra|Anambara|Bauchi|Bayelsa|Benue|"
    "Borno|Cross River|Delta|Ebonyi|Edo|Ekiti|Enugu|Gombe|Imo|Jigawa|Kaduna|Kano|"
    "Katsina|Kebbi|Kogi|Kwara|Lagos|Nasarawa|Niger|Ogun|Ondo|Osun|Oyo|Plateau|"
    "Rivers|Sokoto|Taraba|Yobe|Zamfara"
)
_STATE_SUFFIX = re.compile(rf"\s*[-–—|(]\s*({NIGERIAN_STATES})\s*\)?\s*$", re.I)
_STATE_ALIAS = {
    "anambara": "anambra",   # the board's spelling
    "fct": "abuja",
    "federal capital territory": "abuja",
}
_STATE_LOOKUP = {
    s.lower().replace(" ", ""): s.lower() for s in NIGERIAN_STATES.split("|")
}
_STATE_LOOKUP.update(_STATE_ALIAS)


def strip_location_suffix(title: str) -> str:
    """'Field Service Engineer (Solar Energy) - Delta'
       -> 'Field Service Engineer (Solar Energy)'"""
    return _STATE_SUFFIX.sub("", title).strip(" -–—|")


def canonical_state(raw: str) -> str | None:
    """Map a raw location string to a canonical state, or None.

    Token-based, not substring: 'Lagos, Nigeria' and 'Kano State' both resolve.
    Handles the 'Anambara' typo — a literal regex silently splits one role
    into two clusters, reproduced during recon.
    """
    for tok in re.split(r"[^A-Za-z]+", raw.lower()):
        hit = _STATE_LOOKUP.get(tok)
        if hit:
            return hit
    return None


def role_key(job: JobPosting) -> tuple[str, str, str]:
    """Cluster identity: one ROLE spanning N state postings. The match queue
    operates on this, never on a posting."""
    return (
        job.source,
        job.company.lower().strip(),
        re.sub(r"\s+", " ", strip_location_suffix(job.title).lower()),
    )


def _parse_date(raw: str) -> date | None:
    m = re.search(r"([A-Z][a-z]{2})\s+(\d{1,2}),\s*(\d{4})", raw)
    if not m:
        return None
    for fmt in ("%b %d %Y", "%B %d %Y"):
        try:
            return datetime.strptime(" ".join(m.groups()), fmt).date()
        except ValueError:
            continue
    return None


def _parse_experience(raw: str) -> tuple[int | None, int | None]:
    """'5 years' -> (5, None).  '1 - 3 years' -> (1, 3).  '8 - 12 years' -> (8, 12).
    Absent means unspecified, not zero — 50% of postings carry no experience."""
    nums = re.findall(r"\d+", raw or "")
    if not nums:
        return None, None
    vals = [int(n) for n in nums[:2]]
    if len(vals) == 1:
        return vals[0], None
    return vals[0], vals[1]


def _jkey(html: str, label: str) -> str:
    m = re.search(
        r'<span class="jkey-title">\s*' + re.escape(label) + r"\s*</span>\s*"
        r'<span class="jkey-info">(.*?)</span>',
        html,
        re.S,
    )
    return _text(m.group(1)) if m else ""


def parse_job_page(html: str, url: str) -> JobPosting | None:
    """Parse an individual /job/{slug} page."""
    if "Just a moment" in html[:4000]:
        raise BlockedByBotDefense(f"{url} -> interstitial challenge")

    m = re.search(r"<h1[^>]*>(.*?)</h1>", html, re.S)
    title = _text(m.group(1)) if m else ""
    if not title:
        return None

    # h1 format is "{role} at {company}"
    role, _, company = title.partition(" at ")
    if not company:
        role, company = title, ""

    slug = url.rstrip("/").rsplit("/", 1)[-1]

    body_m = re.search(
        r'<div class="job-details[^"]*">(.*?)(?=<div class="(?:apply|read-)|</section>)',
        html,
        re.S,
    )
    jd = _text(body_m.group(1)) if body_m else ""

    posted = deadline = None
    pm = re.search(r'id="posted-date"[^>]*>(.*?)</div>', html, re.S)
    if pm:
        posted = _parse_date(_text(pm.group(1)))
    for dm in re.finditer(
        r'<div class="read-date-sec-li"[^>]*>(.*?)</div>', html, re.S
    ):
        label = _text(dm.group(1))
        if label.lower().startswith("deadline"):
            deadline = _parse_date(label)
            break

    # Company-level estimate, ~70% absent. Captured, never filtered on.
    salary, size = "", None
    sm = re.search(r'salary-widget-amount">(.*?)</div>', html, re.S)
    if sm:
        salary = _text(sm.group(1))
        size_m = re.search(r"from\s+([\d,]+)\s+employees", salary)
        if size_m:
            size = int(size_m.group(1).replace(",", ""))

    experience = _jkey(html, "Experience")
    min_y, max_y = _parse_experience(experience)
    location = _jkey(html, "Location")

    # Canonical hash for change detection only. Never a unique constraint.
    norm = re.sub(r"\s+", " ", re.sub(r"[^a-z ]", " ", jd.lower())).strip()

    return JobPosting(
        source="myjobmag",
        source_job_id=slug,
        url=url,
        title=role.strip() or title.strip(),
        company=company.strip(),
        location=location,
        city=_jkey(html, "City"),
        state_norm=canonical_state(location) or canonical_state(_jkey(html, "City")),
        job_field=_jkey(html, "Job Field"),
        job_type=_jkey(html, "Job Type"),
        qualification=_jkey(html, "Qualification"),
        experience_raw=experience,
        min_years=min_y,
        max_years=max_y,
        posted=posted,
        deadline=deadline,
        jd_body=jd,
        contact_emails=sorted(set(_EMAIL.findall(jd))),
        company_size_estimate=size,
        salary_estimate=salary,
        jd_hash=hashlib.sha256(norm.encode()).hexdigest(),
        raw_html_key=hashlib.sha256(html.encode()).hexdigest(),
    )


def discover_landing_urls(sitemap: str | None = None) -> list[str]:
    """45,001 URLs, approximately newest-first. Order is the freshness signal —
    the file carries no <lastmod>."""
    xml = sitemap if sitemap is not None else _fetch(f"{BASE}/sitemap-main-jobs.xml")
    return re.findall(r"<loc>(.*?)</loc>", xml)


def discover_title_index(sitemap: str | None = None) -> list[str]:
    """45,001 `/jobs-by-title/{slug}` URLs, one per occupation on the board.

    A second, and different, view of the same site. `sitemap-main-jobs.xml` is
    organised by employer and by single posting; this one is organised by
    occupation, which is the axis a job seeker actually browses on. Reading it
    costs one request and turns "find the software jobs" into "read one page".
    """
    xml = sitemap if sitemap is not None else _fetch(f"{BASE}/sitemap-jobtitle.xml")
    return re.findall(r"<loc>(.*?)</loc>", xml)


def title_index_matches(title_urls: list[str], wanted: set[str] | tuple[set[str], ...], limit: int = 5) -> list[str]:
    """The `/jobs-by-title/` pages for a target, best match first.

    Ranked locally from the slug, so ranking costs no requests. The key is *how
    close* a page is, not whether it shares a word: sorting candidates
    alphabetically returned `academic-librarian-software-engineering` ahead of
    `software-engineer` for a "software engineering" target, which is the page the
    user actually wanted, buried below pages about a different job that merely
    mentions the same words.

    So each candidate is scored on two counts, and the smaller pair wins:

      - words of the target the page does not cover, and
      - extra words the page adds on top of the target.

    That makes an exact page like `software-engineer` (0 missing, 0 extra) beat
    `academic-librarian-software-engineering` (0 missing, 2 extra), which in turn
    beats a page that only partly covers the target. The asymmetry is
    deliberate: a page covering the whole target and more is still about the
    target, whereas a page missing part of it may not be about it at all.

    Ties are then broken toward the *shortest* raw slug, which is the one
    closest to what the user typed. Because the qualifier list strips "senior",
    "head" and "manager" from the token set, `software-engineering`,
    `head-software-engineering` and `associate-senior-software-engineer` all score
    (0, 0) and the tiebreak is the only thing separating the page for the job
    from the pages for a senior and a manager version of it.

    The target's own slug is tried first, ahead of anything found in the index.
    The index is not complete: `software-engineer` is a live page returning real
    listings and is absent from the sitemap, while `software-engineering` is
    present. Constructing the page the user would have clicked and falling back
    to the index covers both spellings for the price of one request.
    """
    if not wanted:
        return []

    readings = [r for r in (wanted if isinstance(wanted, tuple) else (wanted,)) if r]
    if not readings:
        return []

    def score(url: str) -> tuple[int, int, int] | None:
        slug = landing_slug(url)
        slug_tokens = tokenise(slug)
        if not slug_tokens:
            return None
        best: tuple[int, int, int] | None = None
        for reading in readings:
            if not (reading & slug_tokens):
                continue
            pair = (
                len(reading - slug_tokens),
                len(slug_tokens - reading),
                len(re.findall(r"[a-z0-9]+", slug.lower())),
            )
            if best is None or pair < best:
                best = pair
        return best

    # Scored once per URL and not twice. This runs over 45,001 URLs for every
    # target on every daily run, and each score is a tokenise call with the whole
    # phrase table applied; computing it twice for the sort and the filter made
    # the step cost double for no gain.
    scored: list[tuple[tuple[int, int, int], str]] = []
    seen: set[str] = set()
    for url in title_urls:
        if url in seen:
            continue
        value = score(url)
        if value:
            seen.add(url)
            scored.append((value, url))
    scored.sort(key=lambda pair: (pair[0], pair[1]))
    return [url for _, url in scored[:limit]]


def target_title_page(target_title: str) -> str:
    """The `/jobs-by-title/` page a person would reach by typing the target in.

    Constructed rather than looked up, because the site's own index is missing
    pages that are live: `/jobs-by-title/software-engineer` serves a full page of
    software roles and does not appear in `sitemap-jobtitle.xml`, while the
    near-identical `software-engineering` does. Both are worth reading, and
    guessing costs one request.
    """
    slug = re.sub(r"[^a-z0-9]+", "-", target_title.lower()).strip("-")
    return f"{BASE}/jobs-by-title/{slug}" if slug else ""


@dataclass
class TargetCrawl:
    """One target's slice of a run: what was yielded, and what was not.

    The counters are the point, and `coverage_complete` is the one that answers
    the question the user actually has — "did I miss anything today?".

    A run that returns three jobs looks identical whether it read 3 pages or
    3,000, and those mean opposite things: the first is a thin match, the second
    a filter that has stopped recognising the board's vocabulary. And when the
    budget is hit partway, three published jobs might mean the board has three
    and might mean there were five hundred and the crawl stopped at forty. Only
    the counters distinguish those, which is why `coverage_complete` is False
    whenever the run stopped early rather than at a natural end.
    """

    jobs: list[JobPosting] = field(default_factory=list)
    next_cursor: int = 0
    landings_examined: int = 0
    postings_seen: int = 0
    skipped_known: int = 0
    skipped_unrelated_title: int = 0
    skipped_too_old: int = 0
    skipped_other_state: int = 0
    undated: int = 0
    wrapped: bool = False
    budget_hit: bool = False
    # Split of where the job links came from. Kept separate because the two
    # sources fail differently, and a single total hides which one went quiet: an
    # occupation page returning nothing while the head is busy is a different
    # problem from the reverse.
    title_landings_examined: int = 0
    title_index_paths: int = 0
    head_paths: int = 0
    # True when the walk reached the end of its window on its own rather than
    # stopping on `max_jobs` or `max_landings`. False means the day's postings
    # are not proven complete, and the next run picks up from next_cursor.
    coverage_complete: bool = False

    @property
    def published(self) -> int:
        return len(self.jobs)


def crawl_target(
    target_title: str,
    *,
    states: list[str] | None = None,
    known_job_ids: set[str] | None = None,
    cursor: int = 0,
    max_jobs: int = 40,
    max_landings: int = 100,
    max_title_pages: int = 4,
    cutoff: date | None = None,
    poll_delay: float = 1.1,
    sitemap: str | None = None,
    stop_after_expired: int = 25,
) -> TargetCrawl:
    """Crawl the jobs matching one target, resuming from `cursor`.

    Discovery is by **recency only, and relevance is applied late**. The sitemap
    is walked from the head — newest postings first — and every landing opened
    is scanned for jobs, with the title filter applied to the parsed detail page
    rather than to anything cheaper.

    This is a deliberate reversal of the previous design, which ranked landings
    by whether their slug contained the target's words and opened only those.
    That was wrong in a way no amount of tuning could fix: the sitemap is mostly
    *company* landings (`/jobs/jobs-at-brit-property-nigeria-3`) and a minority
    are single-job slugs. Filtering on the slug therefore opened the company
    pages that happened to name your occupation and silently skipped the ones
    that did not — including pages full of exactly the jobs being searched for.
    A relevant job behind an unrelated company name was unreachable, and no
    synonym table or window size can recover it.

    Ranking landings by the target's words is a *fetch* decision made from
    incomplete information, and being wrong there is invisible: the job is never
    seen, so it cannot be reported as missing. Ranking after the fetch uses the
    only authoritative signal available, the posted title, and being wrong there
    costs one page. Late filtering is cheaper *and* more complete here, which is
    the unusual case where the two goals stop competing.

    The cost is requests: every landing opened is one the old design might have
    skipped. That is bounded by `max_landings`, and it buys a guarantee the old
    design could not make — that a day's postings are seen regardless of what
    the companies happen to be called.
    """
    from .targeting import (
        job_title_relevant,
        landing_slug,
        matches_state,
        target_token_sets,
        tokenise,
    )

    wanted = target_token_sets(target_title)
    known = known_job_ids or set()
    result = TargetCrawl()

    landings = discover_landing_urls(sitemap)
    if not landings:
        return result

    # The cursor walks *history*. New postings appear at the head of a
    # newest-first sitemap, so a daily run reads from the top and relies on the
    # known-ID set to skip what it has already seen — skipping is a local slug
    # check, not a request. Resuming a daily run from a deep cursor would miss
    # everything posted since, which is the only thing a daily run is for.
    start = (max(0, min(cursor, len(landings) - 1))) if cutoff is not None else 0
    if cutoff is None:
        cursor = 0
    # Walk from the cursor to the end, then wrap once so a target whose cursor
    # has run past the end still re-checks the head for anything genuinely new.
    order = list(range(start, len(landings)))
    if start > 0:
        order += list(range(0, start))
        result.wrapped = True

    # Two sources of job links, because neither alone is complete, and the gap
    # is not small.
    #
    # The head walk is completeness over *time*: every posting made most
    # recently, whatever its occupation. Measured on this board it is nowhere
    # near sufficient on its own — 132 postings read from the head contained not
    # one software engineering role, while 26 were on the board at the time.
    #
    # The title index is completeness over *occupation*: the site's own page for
    # "software engineer", which is where a person looking for that job actually
    # goes. One request, and it is the answer to "what software jobs exist".
    #
    # Each is blind where the other is not: the head is thin on any given
    # occupation because a day's postings are spread across every field, and a
    # title page lags behind postings made minutes ago. So both are read, the
    # title pages first because they are the higher-yield source for the target,
    # and the job links are de-duplicated before anything is fetched.
    title_landings: list[str] = []
    direct = target_title_page(target_title)
    if direct:
        title_landings.append(direct)
    try:
        index_urls = discover_title_index()
        for url in title_index_matches(index_urls, wanted, limit=max_title_pages):
            if url not in title_landings:
                title_landings.append(url)
    except Exception:
        # The title index is an optimisation on top of the head walk, not a
        # dependency: a failure here degrades recall and must not fail the run.
        # The directly-constructed page above still gets tried.
        pass

    head_window = order[:max(1, max_landings)]

    # Which source goes first depends on what the run is for, and getting this
    # wrong starves one of them entirely.
    #
    # A first run is a backfill: the title pages are the complete answer to "what
    # software jobs exist", so they lead and the head walk backfills the rest.
    #
    # A daily run is a delta. The title pages still list every job from last week,
    # and those are all in the known set, so reading them first spends the whole
    # budget re-confirming jobs already stored — while the head walk, which is
    # where a job posted this morning actually appears, never gets reached. The
    # measured case: three title pages filled a ten-job budget and the head walk
    # opened zero landings.
    #
    # So the head leads on a delta, and the title pages follow as the backfill for
    # anything posted since they were last updated.
    from_title = [
        (url, None) for url in title_landings
    ]
    from_head = [(landings[i], i) for i in head_window]
    candidates = (
        from_title + from_head
        if cutoff is not None
        else from_head + from_title
    )

    paths: list[str] = []
    seen_paths: set[str] = set()
    examined = 0
    stopped_at: int | None = None
    budget_hit = False

    for url, index in candidates:
        if examined >= max_landings or len(paths) >= max_jobs * 4:
            budget_hit = True
            if index is not None:
                stopped_at = index
            break

        examined += 1
        result.landings_examined = examined

        try:
            html = _fetch(url)
        except BlockedByBotDefense:
            continue
        except Exception:
            continue

        found = 0
        for path in job_paths_from_landing(html):
            if path in seen_paths:
                continue
            seen_paths.add(path)
            paths.append(path)
            found += 1
        # Which source a link came from, so a run can be read as "26 from the
        # occupation pages, 12 from today's postings" rather than one opaque
        # total. It is also the only way to see when one source has gone quiet.
        if index is None:
            result.title_landings_examined += 1
            result.title_index_paths += found
        else:
            result.head_paths += found
        time.sleep(poll_delay)

    if not budget_hit:
        stopped_at = head_window[-1] + 1 if head_window else None
        result.coverage_complete = True

    result.budget_hit = budget_hit

    expired_streak = 0

    for path in paths:
        if len(result.jobs) >= max_jobs:
            result.budget_hit = True
            break

        slug = path.rsplit("/", 1)[-1]
        if slug in known:
            result.skipped_known += 1
            continue

        url = f"{BASE}{path}"
        try:
            page = _fetch(url)
        except BlockedByBotDefense:
            continue
        except Exception:
            continue

        job = parse_job_page(page, url)
        if not job:
            continue

        result.postings_seen += 1

        # Authoritative title check, on the parsed role rather than the slug.
        if not job_title_relevant(job.title, wanted):
            result.skipped_unrelated_title += 1
            continue

        if cutoff is not None:
            if job.posted is None:
                # Unknown age is not a reason to hide a job. Roughly one in
                # seven postings carries no date, and dropping those would
                # silently remove real openings from a market that is already
                # thin. Counted separately so the operator can see the
                # difference between "recent" and "unverified".
                result.undated += 1
            elif job.posted < cutoff:
                result.skipped_too_old += 1
                expired_streak += 1
                # Only meaningful in head-walk order, where the remainder really
                # is older. A streak rather than a single hit, because the order
                # is only approximately correct and a few re-ordered entries
                # should not end the run early.
                if expired_streak >= stop_after_expired:
                    result.coverage_complete = True
                    return result
                continue
            else:
                expired_streak = 0

        # state_norm, not the raw location string: publish.py sends this same
        # field as the posting's state, so comparing against it is the only
        # way the filter and the stored value can agree.
        if not matches_state(states, job.state_norm or job.location):
            result.skipped_other_state += 1
            continue

        result.jobs.append(job)
        time.sleep(poll_delay)

    # Resuming mid-window would re-open the same landing on the next run, so the
    # cursor is the position *after* the last one considered.
    result.next_cursor = _position_after(landings, stopped_at) if stopped_at is not None else 0
    return result


def _position_after(landings: list[str], index: int | None) -> int:
    """The sitemap position following `index`, wrapping at the end.

    The crawl walks a window that may have wrapped past the end of the sitemap,
    so an index is not always a position in the original ordering. Resolving it
    back to a real position is what keeps the cursor meaningful for the next run.
    """
    if index is None:
        return 0
    n = len(landings)
    if n == 0:
        return 0
    return index % n


def new_landing_urls(known: set[str], head_limit: int = 500) -> list[str]:
    """Read the sitemap head until the first already-known URL.

    Newest-first ordering makes this cheap: typically tens of entries, not
    45,001. See RESEARCH.md §1.3.
    """
    fresh: list[str] = []
    for url in discover_landing_urls()[:head_limit]:
        if url in known:
            break
        fresh.append(url)
    return fresh


def job_paths_from_landing(html: str) -> list[str]:
    """Second hop: returns individual /job/{slug} paths."""
    found = re.findall(r"href='(/job/[^'?#]+)'", html)
    if not found:
        found = re.findall(r'href="(/job/[^"?#]+)"', html)
    return list(dict.fromkeys(found))


def probe(url: str = f"{BASE}/job/instrumentation-manager-pz-cussons") -> dict:
    """Health check. Assert a known page still parses.

    Guards against silent selector drift: a template change on the source can
    yield zero rows with no HTTP error and no exception. Run on a schedule.
    """
    try:
        job = parse_job_page(_fetch(url), url)
    except BlockedByBotDefense as exc:
        return {"ok": False, "degraded": True, "reason": str(exc)}
    except Exception as exc:  # noqa: BLE001
        return {"ok": False, "reason": f"fetch: {exc}"}
    if job is None:
        return {"ok": False, "reason": "page did not parse (selector drift?)"}
    missing = [
        f for f in ("title", "company", "jd_body", "location") if not getattr(job, f)
    ]
    return {
        "ok": not missing,
        "reason": "ok" if not missing else f"empty required fields: {missing}",
        "title": job.title,
        "company": job.company,
        "jd_len": len(job.jd_body),
    }


def crawl(
    limit_landings: int = 25,
    *,
    poll_delay: float = 1.1,
    known_job_ids: set[str] | None = None,
) -> Iterator[JobPosting]:
    """discovery -> landing -> job detail. Yields parsed postings.

    Known job IDs are skipped before the detail fetch, which is what keeps
    repeat polls cheap.
    """
    known = known_job_ids or set()
    for landing in discover_landing_urls()[:limit_landings]:
        try:
            html = _fetch(landing)
        except BlockedByBotDefense:
            continue
        for path in job_paths_from_landing(html):
            slug = path.rsplit("/", 1)[-1]
            if slug in known:
                continue
            url = f"{BASE}{path}"
            try:
                page = _fetch(url)
            except BlockedByBotDefense:
                continue
            job = parse_job_page(page, url)
            if job:
                yield job
            time.sleep(poll_delay)
        time.sleep(poll_delay)


if __name__ == "__main__":
    import json

    print("probe:", json.dumps(probe(), indent=2))
    print()

    landings = [
        f"{BASE}/jobs/field-service-engineers-at-jaza-energy",
        f"{BASE}/jobs/jobs-at-dreamworks-global-logistics-limited-7",
    ]
    postings: list[JobPosting] = []
    for lp in landings:
        for path in job_paths_from_landing(_fetch(lp)):
            job = parse_job_page(_fetch(f"{BASE}{path}"), f"{BASE}{path}")
            if job:
                postings.append(job)

    clusters: dict[tuple, list[JobPosting]] = defaultdict(list)
    for job in postings:
        clusters[role_key(job)].append(job)

    print(
        f"{len(postings)} postings -> {len(clusters)} roles "
        f"({len(postings) - len(clusters)} duplicate match slots suppressed)\n"
    )
    for key, group in sorted(clusters.items(), key=lambda kv: -len(kv[1])):
        states = sorted({j.state_norm or j.location for j in group})
        print(f"  x{len(group)}  {key[1][:26]:28} {key[2][:46]:48}")
        print(f"       states: {', '.join(states)}")
        print(f"       distinct JD bodies: {len({j.jd_hash for j in group})}")
