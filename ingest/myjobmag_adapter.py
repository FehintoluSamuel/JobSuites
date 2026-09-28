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
import re
import time
import urllib.request
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import date, datetime
from typing import Iterator

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
