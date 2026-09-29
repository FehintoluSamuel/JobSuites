"""
Publisher: crawled MyJobMag postings -> JobSuites API.

The API owns the role table; this script is the only writer. It reuses the
adapter's `role_key()` clustering so the API's unique company+title index and
this script's idea of "one role" can never diverge.

Every run reports itself to `POST /api/ingest/runs`, including runs that
published nothing and runs that failed. That is the point: a poll that returns
zero rows with no error is indistinguishable from broken selectors unless it is
recorded, and the dashboard can then say so instead of looking merely quiet.

Order matters and is deliberate: probe, then crawl. A template change on the
source can yield zero rows with no HTTP error and no exception, so the probe
runs first and a failure skips the crawl entirely rather than spending the
poll's requests proving what one page already showed.

What gets crawled is decided by the API, not here. `GET /api/ingest/queue`
returns the target roles that are due — one row per user, carrying the sitemap
cursor and the known-job set's companion from last time — and this script crawls
exactly those. The user list never appears in this file, which is what lets a
user change what they are looking for without touching the schedule.

A first run for a target is a search of the recent past, so it is bounded to a
3-month window. Later runs are deltas: they resume from the stored cursor and
skip job IDs already stored, so "what is new today" costs only the new pages.

Usage (from repo root, using the ingest venv):
    python -m ingest.publish --ingest-key "$INGEST_KEY" --dry-run
    python -m ingest.publish --ingest-key "$INGEST_KEY" --targets 8
    python -m ingest.publish --ingest-key "$INGEST_KEY" --sweep --limit 5  # board-wide

Idempotent: re-sending the same roles refreshes LastSeenAt and adds no rows.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from collections import defaultdict
from datetime import date, datetime, timedelta, timezone
from typing import Iterator

from .myjobmag_adapter import BASE, JobPosting, TargetCrawl, crawl, crawl_target, probe, role_key

SOURCE_KEY = "myjobmag"
SOURCE_NAME = "MyJobMag"


def _to_ingest(cluster: list[JobPosting]) -> dict:
    """One role cluster -> one IngestRole payload. State postings of the same
    role merge into one row; the union of states is sent back up."""
    primary = cluster[0]
    postings = [
        {
            "source": j.source,
            "sourceJobId": j.source_job_id,
            "url": j.url,
            "location": j.location,
            "state": j.state_norm or j.location,
        }
        for j in cluster
    ]
    return {
        "company": primary.company,
        "title": primary.title,
        "field": primary.job_field or None,
        "jobType": primary.job_type or None,
        "qualification": primary.qualification or None,
        "experienceRaw": primary.experience_raw or None,
        "minYears": primary.min_years,
        "maxYears": primary.max_years,
        "description": primary.jd_body,
        "states": sorted({j.state_norm for j in cluster if j.state_norm}),
        "contactEmails": sorted({e.lower() for j in cluster for e in j.contact_emails}),
        "roleSkills": [],  # the API derives requirements from the JD text itself
        "salaryEstimate": primary.salary_estimate or None,
        "postedAt": primary.posted.isoformat() if primary.posted else None,
        "deadlineAt": primary.deadline.isoformat() if primary.deadline else None,
        "postings": postings,
    }


def cluster(postings: list[JobPosting]) -> Iterator[dict]:
    groups: dict[tuple, list[JobPosting]] = defaultdict(list)
    for job in postings:
        groups[role_key(job)].append(job)
    for group in groups.values():
        yield _to_ingest(group)


def publish(roles: list[dict], api: str, ingest_key: str) -> dict:
    body = json.dumps(roles).encode()
    req = urllib.request.Request(
        f"{api}/api/ingest/roles",
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Ingest-Key": ingest_key,
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as res:
            return json.load(res)
    except urllib.error.HTTPError as exc:
        raise RuntimeError(f"{exc.code}: {exc.read().decode(errors='replace')}") from exc


def report_run(
    api: str,
    ingest_key: str,
    *,
    status: str,
    postings_seen: int = 0,
    roles_published: int = 0,
    started_at: datetime | None = None,
    finished_at: datetime | None = None,
    probe_detail: str | None = None,
    error: str | None = None,
    crawl_target_id: str | None = None,
    user_id: str | None = None,
    target_title: str | None = None,
    cursor: int | None = None,
) -> dict | None:
    """Tell the API what this poll did.

    Best effort: a reporting failure must not mask the run it was reporting, so
    it is logged and the crawl's own outcome still decides the exit code. The
    one case that is not best effort is an unauthenticated call, which is
    reported loudly because it means the run is invisible to the dashboard.

    When the run served one target, `crawl_target_id` and `cursor` are what make
    the next run incremental: the API stores the cursor and hands it back on the
    next queue request, so a daily crawl resumes rather than restarting. The
    cursor is only reported for a successful run — advancing it past a run that
    failed would skip the ground it never actually read.
    """
    payload = {
        "sourceKey": SOURCE_KEY,
        "sourceName": SOURCE_NAME,
        "baseUrl": BASE,
        "startedAt": (started_at or datetime.now(timezone.utc)).isoformat(),
        "finishedAt": (finished_at or datetime.now(timezone.utc)).isoformat(),
        "postingsSeen": postings_seen,
        "rolesPublished": roles_published,
        "status": status,
        "probeDetail": probe_detail,
        "error": error,
        "crawlTargetId": crawl_target_id,
        "userId": user_id,
        "targetTitle": target_title,
        "cursor": cursor if status == "ok" else None,
    }

    req = urllib.request.Request(
        f"{api}/api/ingest/runs",
        data=json.dumps(payload).encode(),
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Ingest-Key": ingest_key,
        },
    )

    try:
        with urllib.request.urlopen(req, timeout=30) as res:
            return json.load(res)
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode(errors="replace")
        print(f"publish: could not record the run ({exc.code}): {detail}", file=sys.stderr)
        return None
    except OSError as exc:
        print(f"publish: could not record the run: {exc}", file=sys.stderr)
        return None


def _get_json(api: str, path: str, ingest_key: str) -> dict:
    """GET with the ingest key, as JSON. A failure here is fatal to the run
    rather than best effort: without the queue there is nothing to crawl, and
    guessing a target would defeat the point of asking."""
    req = urllib.request.Request(
        f"{api}{path}",
        headers={"X-Ingest-Key": ingest_key, "Accept": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=30) as res:
        return json.load(res)


def crawl_one_target(
    api: str,
    ingest_key: str,
    target: dict,
    *,
    known_ids: set[str],
    first_crawl_days: int,
    poll_delay: float,
    dry_run: bool,
    probe_detail: str | None,
    out_dir: str | None,
    max_landings: int,
) -> tuple[list[dict], int, set[str]]:
    """Crawl and publish for one target. Returns (roles, published, slugs seen).

    Each target is its own unit of work with its own run record, because
    "software engineering" and "nursing" fail for entirely different reasons and
    one rolled-up run number would hide which of them went quiet.
    """
    title = target["title"]
    started = datetime.now(timezone.utc)
    first_run = bool(target.get("firstRun"))
    states = target.get("states") or []

    # The recency window applies to the first crawl, which is a search of the
    # recent past and has to bound itself. A later run is a delta: it resumes
    # from the cursor and relies on the known-ID set to skip what it has already
    # seen, so re-applying a 3-month window to it would only discard roles the
    # user is still being shown.
    cutoff = date.today() - timedelta(days=first_crawl_days) if first_run else None

    print(
        f"\n-- {title} ({'first run' if first_run else 'daily'}, "
        f"cursor={target.get('cursor', 0)}, states={','.join(states) or 'any'})",
        file=sys.stderr,
    )

    result: TargetCrawl
    try:
        result = crawl_target(
            title,
            states=states,
            known_job_ids=known_ids,
            cursor=int(target.get("cursor") or 0),
            max_jobs=int(target.get("maxJobs") or 40),
            max_landings=max_landings,
            cutoff=cutoff,
            poll_delay=poll_delay,
        )
    except Exception as exc:  # noqa: BLE001
        report_run(
            api,
            ingest_key,
            status="failed",
            started_at=started,
            probe_detail=probe_detail,
            error=f"crawl: {exc}",
            crawl_target_id=target.get("id"),
            user_id=target.get("userId"),
            target_title=title,
        )
        raise

    print(
        f"   landings={result.landings_examined} seen={result.postings_seen} "
        f"kept={result.published} | dropped: {result.skipped_known} known, "
        f"{result.skipped_unrelated_title} off-target, {result.skipped_too_old} too old, "
        f"{result.skipped_other_state} other state | undated={result.undated} "
        f"budget_hit={result.budget_hit}",
        file=sys.stderr,
    )

    # The completeness line, stated plainly. `seen` is every posting the crawl
    # read today and `kept` is how many matched the target, so the ratio is the
    # honest answer to "did I miss any". When coverage is incomplete the run hit
    # a request budget partway and the rest is not accounted for, so it says so
    # rather than letting a partial read look like a quiet day.
    if result.coverage_complete:
        coverage = "COMPLETE"
    else:
        coverage = f"PARTIAL (hit budget at sitemap position {result.next_cursor})"
    print(
        f"   coverage: {coverage} | {result.postings_seen} postings read today, "
        f"{result.published} matched",
        file=sys.stderr,
    )

    roles = list(cluster(result.jobs))
    published = 0

    if roles and not dry_run:
        api_result = publish(roles, api, ingest_key)
        published = api_result.get("created", 0) + api_result.get("refreshed", 0)
        print(
            f"   published {published} roles "
            f"(created {api_result.get('created', 0)}, refreshed {api_result.get('refreshed', 0)})",
            file=sys.stderr,
        )
    elif roles and dry_run:
        published = len(roles)

    if out_dir and roles:
        import os as _os

        slug = "".join(c if c.isalnum() else "-" for c in title.lower())[:40]
        path = _os.path.join(out_dir, f"{slug}.json")
        with open(path, "w") as f:
            json.dump(roles, f, indent=2)
        print(f"   payload written to {path}", file=sys.stderr)

    # Reported even when the target returned nothing. A search that stops
    # matching the board's vocabulary fails silently, and this is the only
    # record that it did.
    if not dry_run:
        report_run(
            api,
            ingest_key,
            status="ok",
            postings_seen=result.postings_seen,
            roles_published=published,
            started_at=started,
            probe_detail=probe_detail,
            crawl_target_id=target.get("id"),
            user_id=target.get("userId"),
            target_title=title,
            cursor=result.next_cursor,
        )

    return roles, published, {j.source_job_id for j in result.jobs}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--api", default=os.getenv("JOBSUITES_API", "http://localhost:5236"))
    ap.add_argument("--ingest-key", default=os.getenv("INGEST_KEY", ""))
    ap.add_argument(
        "--limit",
        type=int,
        default=25,
        help="landing pages to open per target, or per sweep with --sweep",
    )
    ap.add_argument("--targets", type=int, default=8, help="max targets to serve this run")
    ap.add_argument("--poll-delay", type=float, default=1.1, help="seconds between requests")
    ap.add_argument("--dry-run", action="store_true", help="crawl but never POST")
    ap.add_argument("--out", help="write a single JSON payload here instead of POSTing")
    ap.add_argument("--out-dir", help="write one JSON payload per target here")
    ap.add_argument(
        "--sweep",
        action="store_true",
        help="board-wide crawl, ignoring target roles (no users configured yet)",
    )
    ap.add_argument(
        "--skip-probe",
        action="store_true",
        help="crawl without the health check (for testing the crawler itself)",
    )
    args = ap.parse_args(argv)

    if not args.dry_run and len(args.ingest_key) < 32:
        print("publish: INGEST_KEY is required and must be >= 32 chars.", file=sys.stderr)
        return 2

    api = args.api.rstrip("/")
    started = datetime.now(timezone.utc)

    # Probe first. One page that still parses is enough to trust the selectors;
    # a page that does not means the crawl would only burn requests to confirm
    # it, and would leave the roles table silently stale if it were allowed to
    # finish and publish nothing.
    probe_detail = None
    if not args.skip_probe:
        health = probe()
        detail = health.get("reason", "ok")
        probe_detail = json.dumps(health)[:500]
        print(f"probe: {'ok' if health['ok'] else 'FAILED'} ({detail})", file=sys.stderr)

        if not health["ok"]:
            report_run(
                api,
                args.ingest_key,
                status="blocked" if health.get("degraded") else "degraded",
                started_at=started,
                probe_detail=probe_detail,
                error=f"probe failed: {detail}",
            )
            return 1

    # ---- sweep mode: no user targeting, just walk the head of the board ----
    if args.sweep:
        print(f"crawling {args.limit} landing pages (sweep)...", file=sys.stderr)
        try:
            postings = list(crawl(limit_landings=args.limit))
        except Exception as exc:  # noqa: BLE001
            report_run(
                api, args.ingest_key, status="failed",
                started_at=started, probe_detail=probe_detail, error=f"crawl: {exc}",
            )
            raise

        roles = list(cluster(postings))
        print(f"clustered {len(postings)} postings into {len(roles)} roles", file=sys.stderr)

        if args.out:
            with open(args.out, "w") as f:
                json.dump(roles, f, indent=2)
            print(f"payload written to {args.out}", file=sys.stderr)
        elif args.dry_run:
            print(json.dumps(roles, indent=2))
            return 0

        if not args.dry_run and roles:
            print("api:", json.dumps(publish(roles, api, args.ingest_key)))

        if not args.dry_run:
            report_run(
                api, args.ingest_key, status="ok",
                postings_seen=len(postings), roles_published=len(roles),
                started_at=started, probe_detail=probe_detail,
            )
        return 0

    # ---- targeted mode ------------------------------------------------------
    # The queue is the source of truth for what to crawl. Nothing here knows the
    # user list; asking is what keeps a user editing their target roles from
    # needing a change to this script or to the schedule.
    queue = _get_json(api, f"/api/ingest/queue?limit={args.targets}", args.ingest_key)
    targets = list(queue.get("targets") or [])
    first_crawl_days = int(queue.get("firstCrawlDays") or 90)

    if not targets:
        print(
            "no targets are due: every user target has been crawled within the "
            "last interval, or no profile lists a target role. Nothing to do.",
            file=sys.stderr,
        )
        return 0

    # Fetched once for the whole run, not per target: one user's crawl warms the
    # board for the next user's target, and re-reading the table N times would
    # be the slowest part of the run.
    known = set()
    if not args.dry_run:
        try:
            known = set(_get_json(api, "/api/ingest/known-ids", args.ingest_key).get("ids") or [])
        except Exception as exc:  # noqa: BLE001
            # Not fatal: without it the crawl re-fetches and re-publishes, which
            # is wasteful but correct, because publishing is idempotent.
            print(f"could not load known ids, will re-check every job: {exc}", file=sys.stderr)

    print(
        f"serving {len(targets)} target(s); {len(known)} job(s) already known; "
        f"first-crawl window {first_crawl_days} days",
        file=sys.stderr,
    )

    total_published = 0
    all_roles: list[dict] = []
    failures = 0

    for target in targets:
        try:
            roles, published, slugs = crawl_one_target(
                api,
                args.ingest_key,
                target,
                known_ids=known,
                first_crawl_days=first_crawl_days,
                poll_delay=args.poll_delay,
                dry_run=args.dry_run,
                probe_detail=probe_detail,
                out_dir=args.out_dir,
                max_landings=max(1, args.limit),
            )
        except Exception as exc:  # noqa: BLE001
            # One target's failure must not abandon the rest: they are
            # independent searches, and stopping here would leave every
            # remaining user with no run at all today.
            print(f"   target {target.get('title')!r} failed: {exc}", file=sys.stderr)
            failures += 1
            continue

        total_published += published
        all_roles.extend(roles)
        # Two users can want the same job. Adding what this target found to the
        # known set means the second one does not pay to fetch and re-publish it
        # in the same run.
        known |= slugs

    if args.out and all_roles:
        with open(args.out, "w") as f:
            json.dump(all_roles, f, indent=2)
        print(f"payload written to {args.out}", file=sys.stderr)
    elif args.dry_run:
        print(json.dumps(all_roles, indent=2))

    print(
        f"\ndone: {total_published} roles published across {len(targets)} target(s)"
        + (f", {failures} failed" if failures else ""),
        file=sys.stderr,
    )

    # Non-zero only when every target failed: a partial success is a normal
    # outcome for a run that serves several independent searches.
    return 1 if failures == len(targets) and targets else 0


if __name__ == "__main__":
    raise SystemExit(main())