using JobSuites.Api.Models;

namespace JobSuites.Api.Contracts;

public record CreateTailoringRequest(Guid RoleId);

public record ApproveTailoringRequest(bool Approve);

/// <summary>
/// A tailored document as the review screen needs it: the assembled sections,
/// what changed and why, which requirements went uncovered, and the
/// fabrication verdict.
///
/// The verification block is deliberately first-class in the response rather
/// than a field the UI can choose to render. A user approving a document has to
/// see the check result in the same view as the text, or the check is theatre.
/// </summary>
public record TailoredDocumentResponse(
    Guid Id,
    Guid RoleId,
    string RoleTitle,
    string RoleCompany,
    int Version,
    string Status,
    IReadOnlyList<TailoredSection> Document,
    string PlainText,
    IReadOnlyList<TailoredChange> Diff,
    IReadOnlyList<RequirementCoverage> Coverage,
    FabricationResult Verification,
    RejectedAiRewrite? RejectedRewrite,
    IReadOnlyList<string> FactRefs,
    int UncoveredMustHave,
    int CoveredRequirements,
    bool IsStale,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt)
{
    public static TailoredDocumentResponse From(
        TailoredDocument d,
        string currentProfileHash) => new(
        d.Id,
        d.RoleId,
        d.Role.Title,
        d.Role.Company,
        d.Version,
        d.Status,
        d.Document,
        Tailoring.TailoringEngine.RenderText(d.Document),
        d.Diff,
        d.Coverage,
        d.Verification,
        d.RejectedRewrite,
        d.FactRefs,
        d.Coverage.Count(c => c.MustHave && !c.Covered),
        d.Coverage.Count(c => c.Covered),
        // A CV edited after the document was written makes the document wrong in
        // substance even though it is unchanged on disk. Saying so is better than
        // letting the user send a CV built from facts they have since corrected.
        IsStale: d.ProfileHash.Length > 0 && d.ProfileHash != currentProfileHash,
        d.ApprovedAt,
        d.CreatedAt);
}

public record TailoringListItem(
    Guid Id,
    Guid RoleId,
    string RoleTitle,
    string RoleCompany,
    int Version,
    string Status,
    bool PassedVerification,
    int UncoveredMustHave,
    bool IsStale,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt);
