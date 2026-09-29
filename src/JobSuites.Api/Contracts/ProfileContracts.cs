using JobSuites.Api.Models;

namespace JobSuites.Api.Contracts;

public record CandidateProfileResponse(
    string FileName,
    string? FullName,
    string? Email,
    string? Phone,
    string? Location,
    string? Headline,
    int? YearsExperience,
    string? Summary,
    string? ProfilePicture,
    string? BannerPicture,
    bool PhotoConsentGiven,
    string? DesiredSalary,
    string? Availability,
    IReadOnlyList<string> TargetRoles,
    IReadOnlyList<string> PreferredStates,
    IReadOnlyList<string> DesiredJobTypes,
    IReadOnlyList<ProfileSkill> Skills,
    IReadOnlyList<ProfileExperience> Experiences,
    IReadOnlyList<ProfileEducation> Education,
    IReadOnlyList<ProfileCertification> Certifications,
    IReadOnlyList<ProfileLanguage> Languages,
    IReadOnlyList<ProfileLink> Links,
    ProfileCompleteness Completeness,
    DateTimeOffset UpdatedAt)
{
    public static CandidateProfileResponse From(CandidateProfile p) => new(
        p.FileName,
        p.FullName,
        p.Email,
        p.Phone,
        p.Location,
        p.Headline,
        p.YearsExperience,
        p.Summary,
        p.ProfilePicture,
        p.BannerPicture,
        p.PhotoConsentGiven,
        p.DesiredSalary,
        p.Availability,
        p.TargetRoles,
        p.PreferredStates,
        p.DesiredJobTypes,
        p.Skills,
        p.Experiences,
        p.Education,
        p.Certifications,
        p.Languages,
        p.Links,
        ProfileCompleteness.Compute(p),
        p.UpdatedAt);
}

/// <summary>
/// How much of the profile is filled in, split by whether the field feeds
/// matching. The dashboard uses this to prompt the user: "fill in what is
/// needed" only works when the product can name what is missing.
/// </summary>
public record ProfileCompleteness(
    int Percent,
    int RequiredMet,
    int RequiredTotal,
    int OptionalMet,
    int OptionalTotal,
    IReadOnlyList<string> Missing)
{
    public static ProfileCompleteness Compute(CandidateProfile p)
    {
        var required = new (string Label, bool Done)[]
        {
            ("Your name", !string.IsNullOrWhiteSpace(p.FullName)),
            ("Contact email", !string.IsNullOrWhiteSpace(p.Email)),
            ("Location", !string.IsNullOrWhiteSpace(p.Location)),
            ("A CV to work from", p.Skills.Count > 0 || p.Experiences.Count > 0),
            ("Skills", p.Skills.Count > 0),
            ("Years of experience", p.YearsExperience is not null),
            ("Target roles", p.TargetRoles.Count > 0),
            ("Preferred states", p.PreferredStates.Count > 0),
            ("Desired job types", p.DesiredJobTypes.Count > 0),
        };

        var optional = new (string Label, bool Done)[]
        {
            ("Headline", !string.IsNullOrWhiteSpace(p.Headline)),
            ("Summary", !string.IsNullOrWhiteSpace(p.Summary)),
            ("Phone", !string.IsNullOrWhiteSpace(p.Phone)),
            ("Photos", !string.IsNullOrWhiteSpace(p.ProfilePicture) || !string.IsNullOrWhiteSpace(p.BannerPicture)),
            ("Education", p.Education.Count > 0),
            ("Certifications", p.Certifications.Count > 0),
            ("Languages", p.Languages.Count > 0),
            ("Professional links", p.Links.Count > 0),
            ("Availability", !string.IsNullOrWhiteSpace(p.Availability)),
            ("Desired salary", !string.IsNullOrWhiteSpace(p.DesiredSalary)),
        };

        var requiredMet = required.Count(r => r.Done);
        var optionalMet = optional.Count(r => r.Done);

        return new ProfileCompleteness(
            Percent: required.Length == 0 ? 0 : (int)Math.Round(100.0 * requiredMet / required.Length),
            RequiredMet: requiredMet,
            RequiredTotal: required.Length,
            OptionalMet: optionalMet,
            OptionalTotal: optional.Length,
            Missing: required.Where(r => !r.Done).Select(r => r.Label).ToList());
    }
}

public record ProfileMediaResponse(
    string? ProfilePicture,
    string? BannerPicture);

public record UpdateProfileRequest(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location,
    string? Headline,
    int? YearsExperience,
    string? Summary,
    string? DesiredSalary,
    string? Availability,
    bool? PhotoConsentGiven,
    IReadOnlyList<string>? TargetRoles,
    IReadOnlyList<string>? PreferredStates,
    IReadOnlyList<string>? DesiredJobTypes,
    IReadOnlyList<ProfileSkill>? Skills,
    IReadOnlyList<ProfileExperience>? Experiences,
    IReadOnlyList<ProfileEducation>? Education,
    IReadOnlyList<ProfileCertification>? Certifications,
    IReadOnlyList<ProfileLanguage>? Languages,
    IReadOnlyList<ProfileLink>? Links);

public record RoleSummary(
    Guid Id,
    string Title,
    string Company,
    string? Field,
    string? JobType,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Skills,
    string? SalaryEstimate,
    int? MinYears,
    int? MaxYears,
    int PostingCount,
    DateTimeOffset? PostedAt,
    DateTimeOffset? DeadlineAt,
    IReadOnlyList<RolePostingResponse> Postings)
{
    public static RoleSummary From(Role r) => new(
        r.Id,
        r.Title,
        r.Company,
        r.Field,
        r.JobType,
        r.States,
        r.Skills,
        r.SalaryEstimate,
        r.MinYears,
        r.MaxYears,
        r.PostingCount,
        r.PostedAt,
        r.DeadlineAt,
        r.PostingCount <= 1
            ? []
            : r.Postings.Select(p => new RolePostingResponse(
                p.Id, p.Url, p.Location, p.State, p.Source)).ToList());
}

public record RolePostingResponse(
    Guid Id,
    string Url,
    string? Location,
    string? State,
    string Source);

/// <summary>One role as shown on the dashboard: the score, the reasons behind it,
/// and the gaps worth knowing about before the user invests time in applying.</summary>
public record MatchedRole(
    Guid RoleId,
    string Title,
    string Company,
    string Tier,
    int Score,
    IReadOnlyList<string> States,
    IReadOnlyList<string> RoleSkills,
    string? SalaryEstimate,
    int? MinYears,
    int? MaxYears,
    int PostingCount,
    string? Description,
    IReadOnlyList<MatchEvidenceResponse> Evidence,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<RolePostingResponse> Postings,
    DateTimeOffset? PostedAt,
    DateTimeOffset? DeadlineAt);

public record MatchEvidenceResponse(
    string Kind,
    string Label,
    string Detail,
    string? SourceUrl);

public record DashboardResponse(
    CandidateProfileResponse? Profile,
    DashboardSummary Summary,
    IReadOnlyList<MatchedRole> Matches,
    bool HasIngestedRoles,
    IngestHealth? Ingest);

public record DashboardSummary(
    int RolesIngested,
    int RolesConsidered,
    int StrongMatches,
    int GoodMatches,
    int PossibleMatches,
    int StretchMatches,
    DateTimeOffset? RolesUpdatedAt);

/// <summary>
/// How fresh the roles on this dashboard are, and whether that freshness can be
/// trusted.
///
/// This is not decoration. A board that changes its page template yields zero new
/// roles with no error anywhere, and to the user that is indistinguishable from a
/// quiet week. Carrying the poll time and the yield on the same screen as the
/// queue is what makes the two tellable apart.
/// </summary>
public record IngestHealth(
    DateTimeOffset? LastPolledAt,
    int SourcesTotal,
    int SourcesDegraded,
    int ConsecutiveZeroRuns,
    IReadOnlyList<SourceHealth> Sources);

public record SourceHealth(
    string Key,
    string Name,
    string Status,
    DateTimeOffset? LastPolledAt,
    int? LastYield,
    int ConsecutiveZeroRuns);