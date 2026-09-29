namespace JobSuites.Api.Models;

/// <summary>
/// A CV rewritten for one specific role.
///
/// docs/PRODUCT.md §6 is strict about what this record may contain: selection,
/// ordering and reframing of facts the user has already given us, and nothing
/// else. The generator cannot assert a fact because it is only ever handed a
/// closed set of strings that came from the candidate's own profile, and
/// <see cref="JobSuites.Api.Tailoring.FabricationCheck"/> re-reads the finished
/// text and rejects anything that does not trace back to that set.
/// </summary>
public class TailoredDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Increments on every regeneration so an approved document can
    /// never be silently replaced by a later draft.</summary>
    public int Version { get; set; } = 1;

    /// <summary>draft | needs_review | approved | rejected.
    /// "needs_review" is the default landing state for anything the fabrication
    /// check flagged — a document with an unresolved invention is not a draft,
    /// it is a hazard, and the user has to acknowledge it explicitly.</summary>
    public string Status { get; set; } = "draft";

    /// <summary>The assembled document, section by section. Every string in here
    /// is either verbatim profile text or a template wrapper around it.</summary>
    public List<TailoredSection> Document { get; set; } = [];

    /// <summary>What changed relative to the master CV, and which fact drove
    /// each change. Shown as an inline review so approval is informed.</summary>
    public List<TailoredChange> Diff { get; set; } = [];

    /// <summary>Must-have requirements, split into covered and uncovered.
    /// Gaps are shown rather than hidden — docs/PRODUCT.md §6.3.</summary>
    public List<RequirementCoverage> Coverage { get; set; } = [];

    /// <summary>Result of the deterministic fabrication pass over the finished
    /// text. A clean result is the feature; see docs/PRODUCT.md §6.2.</summary>
    public FabricationResult Verification { get; set; } = FabricationResult.Clean();

    /// <summary>An AI rewrite that failed the fabrication check, kept so the user
    /// can read exactly what the model claimed. It is a diagnosis, never an
    /// artifact: <see cref="Document"/> remains the deterministic build and is
    /// the only thing that can be approved.</summary>
    public RejectedAiRewrite? RejectedRewrite { get; set; }

    /// <summary>Every profile fact the document draws on, so the user can jump
    /// from a line in the document to the fact it came from.</summary>
    public List<string> FactRefs { get; set; } = [];

    /// <summary>Profile content hash the document was built against. A changed CV
    /// marks the document stale instead of leaving a wrong document approved.</summary>
    public string ProfileHash { get; set; } = "";

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public record TailoredSection(
    string Heading,
    string Body,
    string Source = "cv");

/// <summary>One line in the review diff, with the fact that justifies it.
public record TailoredChange(
    string Kind,
    string Detail,
    string? FactRef = null);

public record RequirementCoverage(
    string Requirement,
    bool MustHave,
    bool Covered,
    string? SupportingFact = null);

/// <summary>
/// The outcome of the fabrication check. Populated by a deterministic pass, never
/// by a model judgement, so the same document always produces the same verdict.
public record FabricationResult(
    bool Passed,
    int ClaimsChecked,
    IReadOnlyList<FabricatedEntity> Fabrications)
{
    public static FabricationResult Clean() => new(true, 0, []);
}

public record FabricatedEntity(
    string Text,
    string Kind,
    string Context);

/// <summary>
/// A rewrite the LLM pass produced and the fabrication check refused.
///
/// Stored rather than deleted, because "we caught it inventing Flutterwave" is
/// the proof the product is selling (docs/PRODUCT.md §6.2) — a user who cannot
/// see what was rejected has been shown a verdict with no evidence behind it.
/// It can never be exported or approved; it exists to be read.
/// </summary>
public record RejectedAiRewrite(
    IReadOnlyList<TailoredSection> Document,
    FabricationResult Verification);
