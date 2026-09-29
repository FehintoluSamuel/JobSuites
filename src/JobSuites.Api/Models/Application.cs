namespace JobSuites.Api.Models;

/// <summary>
/// One tracked application, per role rather than per posting.
///
/// docs/PRODUCT.md §8: the source repeats one role across states, so a candidate
/// who applies to "Instrumentation Manager" in three states has applied once.
/// <c>AppliedAt</c> is set by the user and never by the system — the product
/// submits nothing, and auto-submission is not on the roadmap because the source
/// apply path is CAPTCHA-protected.
/// </summary>
public class Application
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Shortlisted | Applied | Interviewing | Closed</summary>
    public string Status { get; set; } = "Shortlisted";

    public DateTimeOffset? AppliedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>What the user intends to do next, and when. This is the field
    /// that makes a tracker useful rather than a second CV to maintain.</summary>
    public DateTimeOffset? NextActionAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A support ticket. Tiny on purpose — no SLA, no assignment, no attachments
/// (docs/PRODUCT.md §9).
///
/// The reason this is in scope at all is not support load: a
/// <c>data_issue</c> ticket is a direct feed into adapter health. "This job is
/// wrong" is the signal that catches selector drift before the health probe does.
/// </summary>
public class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>bug | data_issue | account | feature_request | other</summary>
    public string Category { get; set; } = "other";

    public string Subject { get; set; } = "";

    /// <summary>open | awaiting_user | resolved | closed</summary>
    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Back-navigation for EF. Endpoints return DTOs, so this never
    /// reaches a serialiser and cannot cycle.</summary>
    public List<SupportMessage> Messages { get; set; } = [];
}

public class SupportMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public string Body { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
