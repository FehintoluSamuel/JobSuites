using System.Text.RegularExpressions;
using JobSuites.Api.Models;

namespace JobSuites.Api.Contracts;

/// <summary>
/// Validation for the editable profile. The server is the last line of defence:
/// the client shows chips and hints, but a malformed request must not persist
/// garbage into a column that feeds matching. Errors are keyed per field so the
/// form can attach each one to its input via aria-describedby.
/// </summary>
public static class ProfileValidation
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static bool ValidateUpdate(UpdateProfileRequest r, out Dictionary<string, string[]> errors)
    {
        errors = [];

        Add(errors, "fullName", ValidateText(r.FullName, 200, optional: true));
        Add(errors, "email", ValidateEmail(r.Email));
        Add(errors, "phone", ValidateText(r.Phone, 60, optional: true));
        Add(errors, "location", ValidateText(r.Location, 200, optional: true));
        Add(errors, "headline", ValidateText(r.Headline, 300, optional: true));
        Add(errors, "summary", ValidateText(r.Summary, 4000, optional: true));
        Add(errors, "desiredSalary", ValidateText(r.DesiredSalary, 120, optional: true));
        Add(errors, "availability", ValidateText(r.Availability, 200, optional: true));

        if (r.YearsExperience is { } years and (< 0 or > 60))
        {
            errors["yearsExperience"] = ["Enter a number between 0 and 60."];
        }

        Add(errors, "targetRoles", ValidateStrings(r.TargetRoles, 12, 100));
        Add(errors, "preferredStates", ValidateStates(r.PreferredStates));
        Add(errors, "desiredJobTypes", ValidateJobTypes(r.DesiredJobTypes));

        Add(errors, "skills", ValidateSkills(r.Skills));
        Add(errors, "experiences", ValidateExperiences(r.Experiences));
        Add(errors, "education", ValidateEducation(r.Education));
        Add(errors, "certifications", ValidateCertifications(r.Certifications));
        Add(errors, "languages", ValidateLanguages(r.Languages));
        Add(errors, "links", ValidateLinks(r.Links));

        return errors.Count == 0;
    }

    private static string[]? ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return EmailRegex.IsMatch(value.Trim()) ? null : ["Enter a valid email address."];
    }

    private static string[]? ValidateText(string? value, int max, bool optional)
    {
        if (string.IsNullOrWhiteSpace(value)) return optional ? null : ["This is required."];
        return value.Trim().Length > max ? [$"Keep it under {max} characters."] : null;
    }

    private static string[]? ValidateStrings(IReadOnlyList<string>? values, int maxCount, int maxLength)
    {
        if (values is null) return null;

        var list = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        var problems = new List<string>();

        if (list.Count > maxCount) problems.Add($"Keep it to {maxCount} items.");
        if (list.Any(v => v.Length > maxLength)) problems.Add($"Each item must be under {maxLength} characters.");
        if (list.Count != list.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            problems.Add("Remove duplicates.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateStates(IReadOnlyList<string>? values)
    {
        if (values is null) return null;

        var list = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        var invalid = list.Where(v => !ProfileTaxonomy.IsState(v)).ToList();

        if (invalid.Count > 0)
        {
            return [$"Not a valid Nigerian state: {string.Join(", ", invalid.Take(3))}."];
        }

        return ValidateStrings(values, 37, 60);
    }

    private static string[]? ValidateJobTypes(IReadOnlyList<string>? values)
    {
        if (values is null) return null;

        var list = values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        var invalid = list.Where(v => !ProfileTaxonomy.IsJobType(v)).ToList();

        if (invalid.Count > 0)
        {
            return [$"Choose from full-time, contract, internship or part-time."];
        }

        return ValidateStrings(values, 4, 30);
    }

    private static string[]? ValidateSkills(IReadOnlyList<ProfileSkill>? values)
    {
        if (values is null) return null;

        var list = values.Select(v => v with { Name = v.Name.Trim() }).ToList();
        var problems = new List<string>();

        if (list.Count > 100) problems.Add("Keep it to 100 skills.");
        if (list.Any(s => s.Name.Length == 0)) problems.Add("Every skill needs a name.");
        if (list.Any(s => s.Name.Length > 60)) problems.Add("Skill names must be under 60 characters.");
        if (list.Any(s => s.Years is < 0 or > 60)) problems.Add("Skill years must be between 0 and 60.");

        var distinct = list.Select(s => ProfileTaxonomy.Normalize(s.Name)).Distinct().Count();
        if (list.Count > 0 && distinct != list.Count) problems.Add("Remove duplicate skills.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateExperiences(IReadOnlyList<ProfileExperience>? values)
    {
        if (values is null) return null;

        var list = values.Where(e => !string.IsNullOrWhiteSpace(e.Title) || !string.IsNullOrWhiteSpace(e.Company)).ToList();
        var problems = new List<string>();

        if (list.Count > 30) problems.Add("Keep it to 30 roles.");
        if (list.Any(e => (e.Title?.Length ?? 0) > 150 || (e.Company?.Length ?? 0) > 150))
            problems.Add("Job title and company must be under 150 characters.");
        if (list.Any(e => (e.Highlights?.Length ?? 0) > 2000))
            problems.Add("Keep each set of highlights under 2000 characters.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateEducation(IReadOnlyList<ProfileEducation>? values)
    {
        if (values is null) return null;

        var list = values.Where(e => !(string.IsNullOrWhiteSpace(e.School)
            && string.IsNullOrWhiteSpace(e.Degree))).ToList();
        var problems = new List<string>();

        if (list.Count > 8) problems.Add("Keep it to 8 entries.");
        if (list.Any(e => (e.School?.Length ?? 0) > 150 || (e.Degree?.Length ?? 0) > 150
                        || (e.FieldOfStudy?.Length ?? 0) > 200 || (e.Details?.Length ?? 0) > 1000))
            problems.Add("Education entries are too long.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateCertifications(IReadOnlyList<ProfileCertification>? values)
    {
        if (values is null) return null;

        var list = values.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        var problems = new List<string>();

        if (list.Count > 20) problems.Add("Keep it to 20 certifications.");
        if (list.Any(c => c.Name.Length > 150 || (c.Issuer?.Length ?? 0) > 150))
            problems.Add("Certification names are too long.");
        if (list.Any(c => c.Year is < 1950 or > 2100))
            problems.Add("Enter a valid year.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateLanguages(IReadOnlyList<ProfileLanguage>? values)
    {
        if (values is null) return null;

        var list = values.Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();
        var problems = new List<string>();

        if (list.Count > 15) problems.Add("Keep it to 15 languages.");
        if (list.Any(l => l.Name.Length > 60)) problems.Add("Language names are too long.");
        if (list.Any(l => !IsProficiency(l.Proficiency)))
            problems.Add("Choose basic, conversational, professional or native.");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static string[]? ValidateLinks(IReadOnlyList<ProfileLink>? values)
    {
        if (values is null) return null;

        var list = values.Where(l => !string.IsNullOrWhiteSpace(l.Url)).ToList();
        var problems = new List<string>();

        if (list.Count > 10) problems.Add("Keep it to 10 links.");
        if (list.Any(l => (l.Label?.Length ?? 0) > 40 || (l.Url?.Length ?? 0) > 500))
            problems.Add("Links are too long.");

        var bad = list.Where(l =>
            !(l.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || l.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))).ToList();

        if (bad.Count > 0)
            problems.Add("Links must be full addresses starting with https://");

        return problems.Count == 0 ? null : problems.ToArray();
    }

    private static bool IsProficiency(string value) => value switch
    {
        "basic" or "conversational" or "professional" or "native" => true,
        _ => false,
    };

    private static void Add(Dictionary<string, string[]> errors, string field, string[]? messages)
    {
        if (messages is not null) errors[field] = messages;
    }
}