"""
Publisher: crawled MyJobMag postings -> JobSuites API.

The API owns the role table; this script is the only writer. It reuses the
adapter's `role_key()` clustering so the API's unique company+title index and
this script's idea of "one role" can never diverge.

Usage (from repo root, using the ingest venv):
    python -m ingest.publish --api http://localhost:5236 --ingest-key "$INGEST_KEY" \
        --limit 10 --out ingest/roles.example.json  # dry run
    cat ingest/roles.json | INGEST_KEY=... python -m ingest.publish --api http://localhost:5236

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
from typing import Iterator

from .myjobmag_adapter import JobPosting, crawl, role_key


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


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--api", default=os.getenv("JOBSUITES_API", "http://localhost:5236"))
    ap.add_argument("--ingest-key", default=os.getenv("INGEST_KEY", ""))
    ap.add_argument("--limit", type=int, default=25, help="landing pages to crawl")
    ap.add_argument("--dry-run", action="store_true", help="cluster but never POST")
    ap.add_argument("--out", help="write the JSON payload here instead of stdin")
    args = ap.parse_args(argv)

    if not args.dry_run and len(args.ingest_key) < 32:
        print("publish: INGEST_KEY is required and must be >= 32 chars.", file=sys.stderr)
        return 2

    print("crawling (this respects the source's polling delay)...", file=sys.stderr)
    postings = list(crawl(limit_landings=args.limit))
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
        result = publish(roles, args.api.rstrip("/"), args.ingest_key)
        print("api:", json.dumps(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())