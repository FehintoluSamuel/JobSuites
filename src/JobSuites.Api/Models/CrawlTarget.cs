namespace JobSuites.Api.Models;

/// <summary>
/// One thing the user is looking for, and the state of the crawl that serves it.
///
/// Exists because a per-user crawler is only affordable if it knows what it is
/// looking for. Without this the crawler reads the board blind and the user
/// filters afterwards, which is the wrong way round: the board is 45,001 URLs
/// and the user's list is a handful of titles, so relevance has to be applied at
/// retrieval or the politeness budget is spent on roles nobody asked for.
///
/// It is a row rather than a value on the profile because it has a lifecycle
/// the profile does not — when it was last crawled, how far through the
/// sitemap we got, and how many consecutive empty polls it has produced. That
/// is the difference between "nothing new today" and "this search has stopped
/// working", and it is only knowable per target.
///
/// Derived from <c>CandidateProfile.TargetRoles</c> rather than entered
/// separately: the profile stays the single place a user says what they want.
/// </summary>
public class CrawlTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>The target role as the user typed it, e.g. "software
    /// engineering". Matched against landing slugs and job titles, so it is kept
    /// as written — normalising it away would lose the words they used.</summary>
    public string Title { get; set; } = "";

    /// <summary>States to prefer, from the profile. Empty means no preference,
    /// not "nowhere" — a user who has not chosen should still see roles.</summary>
    public List<string> States { get; set; } = [];

    /// <summary>Ceiling on job pages fetched per run for this target.
    ///
    /// The politeness budget is the real constraint: every detail page costs a
    /// request and a delay, and a target that finds 300 matches must not spend a
    /// run fetching 300 of them. The remainder is picked up on later runs.</summary>
    public int MaxJobsPerRun { get; set; } = 40;

    public DateTimeOffset? LastCrawledAt { get; set; }

    /// <summary>How far through the sitemap this target has walked. The sitemap
    /// is newest-first, so a cursor is also a time axis: resuming from it is
    /// what makes a daily run incremental instead of re-reading the same head
    /// every day.</summary>
    public int Cursor { get; set; }

    /// <summary>Consecutive successful runs that found nothing. Unlike a failed
    /// run, which is already visible, a quiet one is the signal that this
    /// particular search has stopped matching the board's vocabulary.</summary>
    public int ConsecutiveEmptyRuns { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
