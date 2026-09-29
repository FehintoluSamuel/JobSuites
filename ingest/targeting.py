"""Relevance: deciding which individual job belongs to a target.

The source has no queryable search API that a crawler may use — robots.txt
disallows `/search/jobs?*` and every `?`-bearing path — so relevance is applied
here, against a job's parsed title, after its detail page has been fetched.

That ordering is the important decision, and it is the opposite of what this
module originally did. Discovery used to rank *landing pages* by whether the
page's slug contained the target's words, and open only those. It failed on the
sitemap's actual shape: most entries are company landings
(`/jobs/jobs-at-brit-property-nigeria-3`) and only some are single-job slugs, so
a keyword gate opened the company pages that happened to name the occupation and
never opened the rest — including pages full of exactly the jobs being searched
for. Filtering on information available before the fetch means a wrong guess is
invisible: the job is never seen, so it cannot even be reported as missing.

Filtering on the title, which only exists after the fetch, inverts that. Being
wrong costs one page and shows up in the run's counters instead of vanishing.
Late filtering is the more complete choice here, and also the cheaper one, which
is the rare case where completeness and politeness stop being a trade-off.

So this module is about one narrow question — does this job title name the
occupation the user asked for — and is deliberately built to be consistent
rather than clever: a phrase and its synonyms reduce to the same token however
the user or the board happened to spell it, because the failure mode being
guarded against is a job that silently fails to match.
"""

from __future__ import annotations

import re
from collections.abc import Iterable

# Words that appear in almost every slug or title and so cannot discriminate
# between one occupation and another. Kept deliberately small: every word here is
# a word we are choosing *not* to match on, so a broad stop list quietly shrinks
# what the user asked for.
STOPWORDS = frozenset({
    "a", "an", "the", "at", "of", "for", "and", "or", "in", "on", "to", "with",
    "job", "jobs", "role", "roles", "position", "positions", "vacancy", "vacancies",
    "career", "careers", "opportunity", "opportunities", "hiring", "we", "are",
    "new", "latest", "top", "best", "list",
})

# Seniority and department words that qualify an occupation without changing it.
# A user who asks for "software engineering" wants the senior and the intern
# versions too, so these are removed from both sides before comparing.
QUALIFIERS = frozenset({
    "senior", "snr", "jr", "junior", "lead", "principal", "staff", "associate",
    "graduate", "grad", "entry", "intern", "internship", "trainee", "apprentice",
    "chief", "head", "director", "manager", "executive", "officer", "consultant",
    "contract", "permanent", "temporary", "full", "part", "time", "remote", "hybrid",
    "onsite", "freelance", "intern", "trainee", "engr",
})

# The board writes the same occupation several ways, and a user types it one of
# those ways. Matching the exact string would make "software engineering" miss
# "Software Engineer", which is the single most common phrasing on the site and
# exactly what the user meant.
# Kept small and one-directional per pair so the equivalence stays inspectable:
# a large hand-built thesaurus here would be a second source of truth that
# nobody reviews and that quietly decides who sees what.
# Ordered tuples, not sets: the first element is the canonical form, chosen by
# whoever wrote the group rather than by the alphabet. Canonicalising
# "engineering" to "coder" because that sorts first is a decision nobody made
# deliberately, and the representative is what shows up in logs and in the
# saved query, so it has to be the sensible word.
SYNONYM_GROUPS: tuple[tuple[str, ...], ...] = (
    ("engineer", "engineering", "engineers"),
    ("developer", "development", "developers", "dev", "programmer", "software", "coder"),
    ("accountant", "accounting", "accountants", "accounts"),
    ("analyst", "analytics", "analysis", "analysts"),
    ("designer", "design", "designers"),
    ("manager", "management", "managers"),
    ("administrator", "admin", "administration", "administrative"),
    ("technician", "technicians", "technical", "tech"),
    ("consultant", "consultancy", "consulting", "consultants"),
    ("executive", "executives"),
    ("assistant", "assistants"),
    ("officer", "officers"),
    ("scientist", "science", "scientists"),
    ("nurse", "nursing", "nurses", "midwife"),
    ("teacher", "teaching", "teachers"),
    ("lawyer", "legal", "law", "lawyers"),
    ("marketer", "marketing", "marketers"),
    ("writer", "writing", "writers", "copywriter", "copywriting"),
    ("architect", "architecture", "architects"),
    ("planner", "planning", "planners"),
    ("buyer", "buying", "purchasing", "procurement"),
    ("driver", "drivers", "driving", "truck", "lorry"),
    ("chef", "chefs", "cook", "cooking", "kitchen"),
    ("receptionist", "reception"),
    ("salesperson", "sales", "salesman", "saleswoman"),
    ("banker", "banking", "banks"),
    ("clerk", "clerical"),
    ("welder", "welding", "welders"),
    ("fitter", "fitting", "fitters"),
    ("electrician", "electrical", "electricians"),
    ("plumber", "plumbing", "plumbers"),
    ("carpenter", "carpentry", "carpenters"),
    ("operator", "operators", "operation"),
    ("supervisor", "supervisory", "supervisors"),
    ("pharmacist", "pharmacy"),
    ("physician", "doctor", "medical"),
    ("attendant", "attendants"),
)

# token -> the group it belongs to, or None when it belongs to no group.
#
# Built as a dict, so a token appearing in two groups would silently keep only
# the last one — which is how "nurse" ended up canonicalised to "midwife" and a
# search for nursing matched nothing. Overlapping groups are therefore a startup
# error, not a data-quality nit: the table is hand-written, and a hand-written
# table that cannot express the overlap is the safer place to find out.
_seen_tokens: set[str] = set()
for _group in SYNONYM_GROUPS:
    _clash = _seen_tokens & set(_group)
    if _clash:
        raise AssertionError(
            f"synonym groups overlap on {sorted(_clash)}: a token can only "
            "canonicalise to one group"
        )
    _seen_tokens |= set(_group)

_SYNONYM_OF: dict[str, str] = {
    token: group[0] for group in SYNONYM_GROUPS for token in group
}

# Multi-word occupations whose words land in *different* synonym groups, so the
# per-token canonicalisation above cannot reconcile them and the conjunction
# rejects a job the user plainly asked for.
#
# "software engineering" tokenises to {engineer, developer} because "engineering"
# is in the engineer group while "software" is in the developer group. A posting
# titled "Software Developer" — the single most common phrasing on this board for
# that search — tokenises to {developer} alone, so `wanted <= have` fails and the
# job is dropped. The user searching "software engineering" is asking for
# software developer roles; they are the same job.
#
# Collapsing the phrase to one token before tokenising is applied to the target
# and the title alike, so both sides of the comparison move together and the
# asymmetry cannot reappear on one side only.
#
# Kept short and explicit rather than solved generally. A general rule that
# "engineering and software together imply developer" would be harder to review
# than the four phrases it actually needs to cover.
PHRASE_ALIASES: dict[str, str] = {
    "software engineer": "softwaredeveloper",
    "software engineering": "softwaredeveloper",
    "software developer": "softwaredeveloper",
    "software development": "softwaredeveloper",
    "software programmer": "softwaredeveloper",
    "software coder": "softwaredeveloper",
    "web developer": "webdeveloper",
    "web development": "webdeveloper",
    "web design": "webdeveloper",
    "web designer": "webdeveloper",
    "front end developer": "webdeveloper",
    "front end engineering": "webdeveloper",
    "frontend developer": "webdeveloper",
    "full stack developer": "webdeveloper",
    "fullstack developer": "webdeveloper",
    "registered nurse": "nurse",
    "registered nursing": "nurse",
    "project management": "projectmanager",
    "project manager": "projectmanager",
}

# Longest phrase first, so "software development engineer" is matched by the
# longest applicable phrase before the shorter "software developer" can consume
# part of it and leave a fragment behind.
_PHRASE_PATTERNS: tuple[tuple[re.Pattern[str], str], ...] = tuple(
    (re.compile(rf"\b{re.escape(phrase)}\b"), canonical)
    for phrase, canonical in sorted(
        PHRASE_ALIASES.items(), key=lambda kv: -len(kv[0].split())
    )
)

# Non-words we accept inside a slug.
_WORD = re.compile(r"[a-z0-9]+")


def _collapse_phrases(text: str) -> str:
    """Rewrite known multi-word occupations into one canonical token.

    The replacement is a single unbroken word so the normal tokeniser keeps it
    intact, and it is applied to text that has already been lowercased.
    """
    for pattern, canonical in _PHRASE_PATTERNS:
        text = pattern.sub(canonical, text)
    return text


def tokenise(text: str, *, collapse_phrases: bool = True) -> set[str]:
    """Lowercase word set with stopwords, qualifiers and trailing plurals removed.

    Plurals are folded so "engineers" and "engineer" are one token. The fold is
    crude on purpose — a real stemmer would merge words that are not the same
    occupation, and the cost of a false merge is a landing that gets opened.

    `collapse_phrases=False` skips the phrase step, which yields the reading of
    the text exactly as it was written. Both readings are needed: see
    `target_token_sets`.
    """
    low = text.lower().replace("&", " and ")
    # Slugs are hyphen- or underscore-separated, so "software-engineer" would
    # otherwise never match a phrase written with a space and the phrase table
    # would silently do nothing on exactly the input that needed it most: the
    # sitemap slugs. The word regex only ever matched alphanumerics anyway, so
    # widening the separators cannot change which words are found.
    low = re.sub(r"[-_]+", " ", low)
    words = _WORD.findall(_collapse_phrases(low) if collapse_phrases else low)
    out: set[str] = set()
    for word in words:
        if word in STOPWORDS:
            continue
        # "engineering" -> "engineer" is not a plural fold, so it is handled by
        # the synonym groups below; this only strips genuine plurals.
        if len(word) > 3 and word.endswith("ies"):
            word = word[:-3] + "y"
        elif len(word) > 3 and word.endswith("s") and not word.endswith("ss"):
            word = word[:-1]
        if len(word) < 2:
            continue
        # Canonicalise *before* testing against the qualifiers, not after. The two
        # lists are written independently, so canonicalisation can land on a word
        # that is itself a qualifier: "manager" is one, and "management"
        # canonicalises to it. Checking the raw word first meant "Project Manager"
        # lost the noun and reduced to {project} while "Project Management" kept
        # it as {manager, project} — the same occupation reading two different
        # ways depending only on which spelling the user typed, and the
        # conjunction then silently dropped whichever form lost the word.
        word = _canonical(word)
        if word in QUALIFIERS:
            continue
        out.add(word)
    return out


def _canonical(token: str) -> str:
    """Map a token onto the first member of its synonym group.

    Mapping every member to the same representative is what makes
    "engineering" and "engineer" compare equal without either string appearing
    literally in both.
    """
    return _SYNONYM_OF.get(token, token)


def target_tokens(target: str) -> set[str]:
    """The occupations a user asked for, as comparable tokens.

    Empty when the text is only qualifiers or stopwords ("senior manager" ->
    {"manager"} is fine, but "senior" alone is not a job) — the caller treats
    that as "cannot target" and falls back to a board-wide sweep rather than
    guessing.
    """
    return tokenise(target)


def target_token_sets(target: str) -> tuple[set[str], set[str]]:
    """Both readings of a target: with phrases collapsed, and as written.

    Collapsing phrases is what lets "software engineering" match a posting
    titled "Software Developer". But collapsing *tightens* the filter for every
    phrasing that does not match a table entry: the target "Project Management"
    becomes the single token `projectmanager`, and the board's own "Projects
    Manager" — which the plural fold reduces to `{project, manager}` and no
    phrase rule claims — stops matching, where before the phrase table existed it
    did.

    Filtering now happens late, after a request has already been spent, so a
    missed job costs a real page and a real gap in the user's results. Taking
    both readings and accepting either is the response: each is individually
    sound, their union cannot match something neither would have, and a target
    that is genuinely narrower than both still does not match.
    """
    return tokenise(target), tokenise(target, collapse_phrases=False)


def landing_slug(url: str) -> str:
    """`/jobs/software-engineers-at-acme` -> `software-engineers-at-acme`."""
    return url.rstrip("/").rsplit("/", 1)[-1]


def job_title_relevant(title: str, wanted: set[str] | tuple[set[str], ...]) -> bool:
    """Whether an individual job is one of the occupations the user asked for.

    A target is a conjunction: "software engineering" means a role that is both
    software and engineering, and every requested word must appear. The looser
    "match most of the words" rule was tried and rejected — it reported a Civil
    Engineer under a "Mechanical Engineering" target, because the single shared
    word "engineer" was half the request.

    `wanted` may be one token set or the pair from `target_token_sets`, in which
    case either reading satisfying the conjunction is enough.

    Precision is the goal within a reading, but recall is the goal across them.
    The user's complaint that this cannot surface the jobs they need is a recall
    failure, and recall failures are the ones that are invisible: a job that was
    never matched looks identical to a job that was never posted. Erring toward
    showing a near-miss is recoverable — the user reads past it — while a silent
    omission is not, so the tie goes to including the job.
    """
    if not wanted:
        # An untargeted sweep: the date and state rules apply, so the user is not
        # flooded with the whole board, but nothing is filtered on title.
        return True

    readings = wanted if isinstance(wanted, tuple) else (wanted,)
    have = tokenise(title)
    if not have:
        return False

    return any(reading <= have for reading in readings if reading)


def matches_state(states: Iterable[str] | None, state: str | None) -> bool:
    """Whether a posting is in a state the user chose.

    A missing state counts as a match. The source often omits it, and treating
    an absent value as a mismatch would hide the most recent postings — exactly
    the ones a daily crawl is for. An empty preference also matches everything:
    "no preference" is not "nowhere".
    """
    if not states:
        return True
    if not state:
        return True
    wanted = {s.strip().lower() for s in states if s and s.strip()}
    if not wanted:
        return True
    return state.strip().lower() in wanted
