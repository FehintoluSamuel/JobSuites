using JobSuites.Api.Models;

namespace JobSuites.Api.Contracts;

/// <summary>
/// The question set for one role, plus the gaps it was built from.
///
/// <c>Cached</c> is exposed so the UI can say whether the user is looking at a
/// stored set or one generated on this request, which is the observable half of
/// the amortisation claim in docs/PRODUCT.md §7.1.
/// </summary>
public record PrepResponse(
    Guid RoleId,
    string RoleTitle,
    string RoleCompany,
    IReadOnlyList<PrepQuestion> Questions,
    IReadOnlyList<RequirementCoverage> Coverage,
    IReadOnlyList<string> Uncovered,
    bool Cached,
    DateTimeOffset CreatedAt);

public record StartSessionRequest(Guid RoleId, string Mode, int? ItemCount);

/// <summary>
/// One shape for both answer kinds, because a single endpoint cannot bind two
/// complex request bodies — ASP.NET has no way to choose between them. The
/// session already records which kind of answer it wants, so the server
/// dispatches on that and the client sends only the fields that matter:
/// <c>{questionId, body}</c> for gap practice, <c>{itemId, selectedOption}</c>
/// for the aptitude bank.
/// </summary>
public record AnswerRequest(
    string? QuestionId = null,
    string? Body = null,
    string? ItemId = null,
    int? SelectedOption = null);

/// <summary>An aptitude item as served. Deliberately has no answer key — the
/// key and the worked steps come back only after the candidate answers.</summary>
public record AssessmentItemResponse(
    string Id,
    string Domain,
    int Difficulty,
    string Prompt,
    IReadOnlyList<string> Options);

public record AptitudeItemResult(
    string ItemId,
    string Domain,
    bool Correct,
    int AnswerKey,
    string WorkedSteps);

public record PrepSessionResponse(
    Guid Id,
    Guid RoleId,
    string RoleTitle,
    string Company,
    string Mode,
    IReadOnlyList<AssessmentItemResponse> Items,
    IReadOnlyList<DomainScore> Scores,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public record SubmitAnswerResponse(
    string Verdict,
    bool HonestFramingUsed,
    AptitudeItemResult? Item);

public record PrepSessionSummary(
    Guid Id,
    Guid RoleId,
    string RoleTitle,
    string Company,
    string Mode,
    IReadOnlyList<DomainScore> Scores,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
