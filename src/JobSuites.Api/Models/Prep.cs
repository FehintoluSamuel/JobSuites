namespace JobSuites.Api.Models;

/// <summary>
/// Interview questions for one role, built from one candidate's CV and cached
/// for them.
///
/// The obvious design here is to key this on the role alone, so generation is
/// paid once and every candidate is served the same set. That does not work:
/// whether a requirement is a "gap" question or a "strength" question is
/// decided by the join between the posting and that person's CV, and so are the
/// framing and the adjacent experience named inside the answer. A role-only key
/// means the first candidate to ask populates the cache, and everyone after them
/// is shown the first candidate's gaps — which is both wrong advice and a
/// cross-account data leak.
///
/// So the cache is keyed on role *and* user. The amortisation claim in
/// docs/PRODUCT.md §7.1 survives in the form that actually holds: a candidate
/// pays for generation once per role and is served from cache on every later
/// visit, and preparation is still never re-run per question or per session.
/// </summary>
public class InterviewPrep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>One prep record per (role, candidate). The uniqueness of this is
    /// what makes the cache correct rather than merely intended.</summary>
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Requirement categories the questions were built around, so a
    /// caller can tell a cached set from one generated for different gaps.</summary>
    public List<string> Topics { get; set; } = [];

    public List<PrepQuestion> Questions { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One question plus the honest answer shape it should be answered in.
///
/// docs/PRODUCT.md §7.4 is a hard constraint: prep must never coach a user into
/// claiming experience they do not have. <see cref="HonestFraming"/> is therefore
/// not a suggestion, it is the answer structure the UI shows alongside the
/// question, and it always names transferable work instead of asserting the
/// missing skill.
/// </summary>
public record PrepQuestion(
    string Id,
    string Kind,
    string Prompt,
    string Gap,
    string HonestFraming,
    string? AdjacentEvidence = null,
    // The specific missing term this question is about, e.g. "Communication".
    // Kept separately from Gap because Gap is prose written for a human ("No
    // evidence of Communication in your CV.") and prose cannot be reliably
    // parsed back into a term to check an answer against. Null for question
    // kinds that have no single term, which is also what tells the answer check
    // to stay quiet rather than guess.
    string? MissingTerm = null);

/// <summary>An answered practice question inside a session.</summary>
public class PrepAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public PrepSession Session { get; set; } = null!;

    public string QuestionId { get; set; } = "";

    /// <summary>Free-text answer, or the chosen option for an aptitude item.</summary>
    public string Body { get; set; } = "";

    public string Verdict { get; set; } = "";

    /// <summary>True when the answer leaned on adjacent experience rather than
    /// claiming the missing skill. Surfaced to the user so the honest path is
    /// the visible one.</summary>
    public bool HonestFramingUsed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One practice run. Tier 2 is timed and scored deterministically, reusing the
/// same categorical verdict vocabulary as matching rather than a percentage.
/// </summary>
public class PrepSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>gap_driven | aptitude</summary>
    public string Mode { get; set; } = "gap_driven";

    /// <summary>Per-domain verdicts, e.g. numerical: Strong. Empty until finished.</summary>
    public List<DomainScore> Scores { get; set; } = [];

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}

public record DomainScore(string Domain, int Correct, int Attempted, string Verdict);

/// <summary>
/// An aptitude item. Banked in the database rather than generated, because a
/// scored assessment has to be reproducible: the same item must have the same
/// answer key every time it is served.
/// </summary>
public class AssessmentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>numerical | verbal | analytical | abstract</summary>
    public string Domain { get; set; } = "";

    public int Difficulty { get; set; } = 1;

    public string Prompt { get; set; } = "";

    public List<string> Options { get; set; } = [];

    /// <summary>Index into <see cref="Options"/>. Never serialised to a client.</summary>
    public int AnswerKey { get; set; }

    /// <summary>Shown after answering, so a wrong answer still teaches.</summary>
    public string WorkedSteps { get; set; } = "";
}
