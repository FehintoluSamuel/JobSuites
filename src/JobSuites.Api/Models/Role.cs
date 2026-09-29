namespace JobSuites.Api.Models;

/// <summary>
/// A canonical role: one real job that may be posted in several states.
///
/// The source board repeats a single role once per location. Deduplicating on
/// (company, title) is what keeps the dashboard from showing ten copies of the
/// same job. See RESEARCH.md §1.7b2 and the adapter's role_key().
/// </summary>
public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Company { get; set; } = "";

    /// <summary>Title with the trailing state suffix removed, e.g.
    /// "Instrumentation Manager - PZ" becomes "Instrumentation Manager".</summary>
    public string Title { get; set; } = "";

    public string? Field { get; set; }
    public string? JobType { get; set; }
    public string? Qualification { get; set; }
    public string? ExperienceRaw { get; set; }
    public int? MinYears { get; set; }
    public int? MaxYears { get; set; }

    /// <summary>States this role is posted in. Empty when the posting gave no
    /// location we could normalise.</summary>
    public List<string> States { get; set; } = [];

    /// <summary>Representative JD body. The role's postings differ by only a few
    /// characters each (the state name), so one body is representative.</summary>
    public string Description { get; set; } = "";

    public List<string> Skills { get; set; } = [];

    public List<string> ContactEmails { get; set; } = [];

    /// <summary>How many separate postings collapsed into this role.</summary>
    public int PostingCount { get; set; }

    public string? SalaryEstimate { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }

    public List<RolePosting> Postings { get; set; } = [];

    /// <summary>What this role asks for, extracted once at ingest and shared by
    /// every user of the role (docs/ARCHITECTURE.md §5.2). Empty until ingest has
    /// run for it; callers fall back to reading the description directly.</summary>
    public List<JdRequirement> Requirements { get; set; } = [];

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RolePosting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoleId { get; set; }

    /// <summary>Back-navigation for EF. Endpoints always return DTOs rather than
    /// entities, so this never reaches a JSON serialiser and cannot cycle.</summary>
    public Role Role { get; set; } = null!;

    public required string Source { get; set; }
    public required string SourceJobId { get; set; }
    public required string Url { get; set; }
    public string? Location { get; set; }
    public string? State { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A computed match between one profile and one role.
///
/// Persisted rather than computed per request: a score is shown to the user as
/// a fact, so we need to be able to explain why it has not changed, and to
/// point at what it was derived from.
/// </summary>
public class RoleMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid JobId { get; set; }
    public Role Job { get; set; } = null!;

    /// <summary>Profile content hash this score was computed against. A changed
    /// CV invalidates the score rather than silently reusing a stale one.</summary>
    public string ProfileHash { get; set; } = "";

    public int Score { get; set; }

    public string Tier { get; set; } = "";

    public List<MatchEvidence> Evidence { get; set; } = [];

    /// <summary>Reasons the score is capped, e.g. "requires 8 years, you show 3".
    /// Explaining a low score matters more than explaining a high one.</summary>
    public List<string> Gaps { get; set; } = [];

    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One traceable reason behind a score. Every entry links back to the
/// posting, because an unexplainable number is worse than no number.</summary>
public record MatchEvidence(
    string Kind,
    string Label,
    string Detail,
    int Points,
    string? RolePostingUrl);
