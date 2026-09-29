namespace JobSuites.Api.Contracts;

public record CreateApplicationRequest(Guid RoleId, string? Status, string? Notes, DateTimeOffset? NextActionAt);

public record UpdateApplicationRequest(
    string? Status,
    string? Notes,
    DateTimeOffset? NextActionAt);

public record ApplicationResponse(
    Guid Id,
    Guid RoleId,
    string RoleTitle,
    string Company,
    string RoleUrl,
    string Status,
    DateTimeOffset? AppliedAt,
    string? Notes,
    DateTimeOffset? NextActionAt,
    string? MatchTier,
    bool HasTailoredDocument,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Counts for the tracker header plus the rows themselves, computed in
/// one pass so the numbers on screen always agree with the list beneath them.</summary>
public record ApplicationSummary(
    int Shortlisted,
    int Applied,
    int Interviewing,
    int Closed,
    int Total,
    IReadOnlyList<ApplicationResponse> Items,
    IReadOnlyList<ApplicationResponse> NextActions);

public record CreateTicketRequest(string Category, string Subject, string? Body);

public record AddTicketMessageRequest(string Body);

public record SupportTicketResponse(
    Guid Id,
    string Category,
    string Subject,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<SupportMessageResponse> Messages);

public record SupportMessageResponse(Guid Id, Guid AuthorId, bool IsMine, string Body, DateTimeOffset CreatedAt);
