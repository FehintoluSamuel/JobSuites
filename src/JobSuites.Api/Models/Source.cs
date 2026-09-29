namespace JobSuites.Api.Models;

/// <summary>
/// A job board we ingest from, and the health of the last few polls of it.
///
/// Exists because a silent failure here is indistinguishable from a quiet
/// market. A template change on the board yields zero new roles with no HTTP
/// error and no exception, so a queue that simply stops filling looks identical
/// to a week with nothing posted (docs/ARCHITECTURE.md §6, §7). The dashboard
/// reads this row, and a run that yields nothing N times in a row is a warning
/// rather than a condition.
///
/// One row per adapter. `Key` is the adapter key the Python side reports, so the
/// health a user sees is tied to the adapter that actually ran.
/// </summary>
public class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Adapter key, e.g. "myjobmag". Stable across restarts, so it is
    /// the natural key for health to accumulate against.</summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";

    public string BaseUrl { get; set; } = "";

    /// <summary>healthy | degraded | blocked.
    ///
    /// "degraded" means the source answered but is untrustworthy — a failed
    /// probe, or N consecutive zero yields. "blocked" means it refused us. The
    /// distinction matters: blocked is a wall we should not push against, and
    /// degraded is a selector we should fix.</summary>
    public string Status { get; set; } = "healthy";

    public DateTimeOffset? LastPolledAt { get; set; }

    /// <summary>Roles created by the most recent run. Null until the first run
    /// reports, which is different from a run that yielded zero.</summary>
    public int? LastYield { get; set; }

    /// <summary>Consecutive completed runs that produced nothing new. Reset by
    /// any run that yields. Three in a row is treated as drift worth warning
    /// about, not as a quiet market.</summary>
    public int ConsecutiveZeroRuns { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public List<IngestRun> Runs { get; set; } = [];
}

/// <summary>
/// One poll of one source. Written for every run, including the ones that
/// published nothing — a run that yielded zero roles is the most informative
/// row in the table, and only exists if failures are recorded as diligently as
/// successes.
/// </summary>
public class IngestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SourceId { get; set; }
    public Source Source { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }

    public int PostingsSeen { get; set; }
    public int RolesPublished { get; set; }

    /// <summary>ok | degraded | failed.
    ///
    /// "degraded" is the state a source behind a bot wall or a failed probe
    /// leaves behind. It is not an error the pipeline stops on: one bad board
    /// must never halt the others.</summary>
    public string Status { get; set; } = "ok";

    /// <summary>Result of the pre-flight probe, verbatim. Kept on the run so the
    /// claim "the page parsed" is checkable later rather than taken on trust.</summary>
    public string? ProbeDetail { get; set; }

    public string? Error { get; set; }

    /// <summary>The user this run served, when it was a targeted crawl.
    /// Null for a board-wide sweep.</summary>
    public Guid? UserId { get; set; }

    public Guid? CrawlTargetId { get; set; }
    public CrawlTarget? CrawlTarget { get; set; }

    /// <summary>The target as the user typed it, denormalised onto the run.
    ///
    /// A run has to stay readable after the target row it came from is edited
    /// or deleted, so "what were we actually searching for at 16:00" must not
    /// require joining through a mutable row.</summary>
    public string? TargetTitle { get; set; }
}
