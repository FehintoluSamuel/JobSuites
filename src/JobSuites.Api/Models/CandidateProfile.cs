namespace JobSuites.Api.Models;

/// <summary>
/// Everything we know about a candidate.
///
/// Two origins feed this record: the CV, which is parsed into facts, and the
/// user, who corrects and extends those facts directly. The product rule from
/// docs/PRODUCT.md §4 is that extraction is correctable before first use, so
/// skills and experience carry a Source: re-uploading a CV replaces the
/// CV-derived facts but never destroys a user's manual correction.
/// </summary>
public class CandidateProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string FileName { get; set; } = "";

    /// <summary>Extracted plain text. Retained so the parse can be re-run
    /// without re-uploading, and so the user can see what we actually read.</summary>
    public string RawText { get; set; } = "";

    /// <summary>SHA-256 of the raw text. Identifies the CV content without
    /// storing the file twice, and lets us skip re-parsing an unchanged upload.</summary>
    public string ContentHash { get; set; } = "";

    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string? Headline { get; set; }

    /// <summary>Total years of experience. Inferred from date ranges on upload;
    /// the user can correct it. Null means unknown — we never guess, because a
    /// wrong number silently corrupts every match score downstream.</summary>
    public int? YearsExperience { get; set; }

    public string? Summary { get; set; }

    /// <summary>Stored file names of user-uploaded images. Empty means none.
    /// Media is served by an owner-guarded endpoint, never a static path.</summary>
    public string? ProfilePicture { get; set; }
    public string? BannerPicture { get; set; }

    /// <summary>Explicit consent to show the user-uploaded photos. docs/DESIGN.md
    /// requires it: a photo without a consent toggle is a product defect.</summary>
    public bool PhotoConsentGiven { get; set; }

    /// <summary>Free-text, e.g. "₦600k/month". Compensation is not used in
    /// matching (the source carries no salary), it is for the user's own
    /// application tracking.</summary>
    public string? DesiredSalary { get; set; }

    /// <summary>Notice period / availability, e.g. "Available immediately".</summary>
    public string? Availability { get; set; }

    /// <summary>Roles the user is targeting, drawn against the board's taxonomy
    /// (role titles of ingested roles) plus free text. Stored as text[] — a
    /// join table would add joins without buying anything for list reads.</summary>
    public List<string> TargetRoles { get; set; } = [];

    /// <summary>Preferred states, from the 36 + FCT taxonomy. Used by location
    /// matching when the CV location is broader than one state.</summary>
    public List<string> PreferredStates { get; set; } = [];

    public List<string> DesiredJobTypes { get; set; } = [];

    /// <summary>JSONB. User-editable, CV-derived items are replaced on re-upload
    /// while manual ones survive — see CvParser.ProfileSkill.Source.</summary>
    public List<ProfileSkill> Skills { get; set; } = [];

    public List<ProfileExperience> Experiences { get; set; } = [];

    public List<ProfileEducation> Education { get; set; } = [];

    public List<ProfileCertification> Certifications { get; set; } = [];

    public List<ProfileLanguage> Languages { get; set; } = [];

    public List<ProfileLink> Links { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Source of a fact. "cv" facts are derived from the uploaded CV and replaced
/// when it is re-uploaded; "manual" facts were entered or corrected by the user
/// and survive re-uploads. The default keeps old JSONB rows valid on read.
/// </summary>
public record ProfileSkill(
    string Name,
    string Normalized,
    int Years,
    bool IsCore,
    string Source = "cv");

public record ProfileExperience(
    string Company,
    string Title,
    string? StartDate,
    string? EndDate,
    bool IsCurrent,
    string? Highlights,
    string Source = "cv");

public record ProfileEducation(
    string? School,
    string? Degree,
    string? FieldOfStudy,
    int? StartYear,
    int? EndYear,
    string? Details,
    string Source = "cv");

public record ProfileCertification(
    string Name,
    string? Issuer,
    int? Year,
    string Source = "cv");

public record ProfileLanguage(
    string Name,
    string Proficiency,
    string Source = "cv");

/// <summary>Professional links: LinkedIn, GitHub, portfolio, X, and so on.</summary>
public record ProfileLink(
    string Label,
    string Url,
    string Source = "cv");