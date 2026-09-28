# Product backlog

Sprint size is deliberately small until the vertical-slice rhythm is proven.
Every story ships frontend, backend, and tests together — a story is not done
until someone can click through the real thing against a real database.

Status: `[x]` done, `[~]` in progress, `[ ]` not started

---

## Sprint 1 — Auth (done)

The point of this sprint was to establish the slice, not to build auth
exhaustively. It also proved out the three-environment setup.

- [x] `POST /api/auth/register` — BCrypt cost 12, email lower-cased, DB unique index
- [x] `POST /api/auth/login` — uniform failure message, constant-time miss path
- [x] `GET /api/auth/me` — requires a valid bearer token
- [x] Signup, sign-in, and authenticated dashboard screens
- [x] Focusable error summary, `aria-invalid`, non-blocking paste, `autocomplete` hints
- [x] Rate limiting on the auth group (10/min per IP)
- [x] 15 API integration tests against a throwaway database
- [x] 12 component tests covering the accessibility contract

Deliberately deferred from this sprint: password reset, email verification,
refresh tokens, and multi-factor auth. Each needs a mail or SMS dependency, and
shipping them before the product has a reason to send mail is wasted work.

---

## Sprint 2 — Profile and the evidence base (done)

The product cannot match anyone until it knows who they are. This sprint is
where the real value starts, and the first place the MyJobMag adapter becomes
useful.

- [x] Profile capture: targets, skills, work history, CV upload
- [x] `Profile` entity and the `user_id` scoping the architecture doc requires
- [x] CV parsing into a structured, user-editable draft
- [x] Ingest pipeline publishes crawled MyJobMag postings through the API
- [x] Read-only roles list on the dashboard, sourced from live MyJobMag data
- [x] Support ticket entity — the user asked for this explicitly, so it is not optional

Decisions made this sprint:

- We store the parsed structure, not the raw CV file. The dashboard tells the
  user this ("your CV is not stored on any server — only the facts we read from
  it are saved"), and content-hashing lets us skip recomputation when an upload
  is unchanged.
- The profile is a single shared document, not versioned per tailoring run.
  Versioning is deferred until tailoring actually rewrites the CV.

---

## Sprint 3 — Matching (done)

- [x] Skill and role extraction from the profile
- [x] Match scoring against ingested postings, with the reason shown
- [x] Ranking and filtering — the salary filter cut from scope is deferred; tier
      filtering shipped instead
- [x] Evidence links back to the source posting for every claim

The evidence links are not cosmetic. Every score has to be traceable to
something the user can verify, or the whole product is just a number.

---

## Sprint 4 — Tailoring and interview prep

- [ ] CV tailoring as closed-set generation
- [ ] Deterministic fabrication check — the non-negotiable. A tailored CV that
      invents an employer or a date is a liability, not a feature.
- [ ] Interview questions generated from the same evidence base
- [ ] Free-text coaching notes, still deterministic

---

## Carried forward from research

- [ ] Browser extension for gated MyJobMag fields. We will not store their
      passwords, so this is the only path to the ~75% of postings that hide
      contact details behind a login.
- [ ] Additional source adapters. The interface is defined; Jooble is blocked by
      Cloudflare and there is no budget for a provider, so this waits on either a
      workaround or a source that cooperates.
- [ ] Support desk triage and SLA tracking once there is real ticket volume.
