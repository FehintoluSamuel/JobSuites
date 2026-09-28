using System.Text.RegularExpressions;
using JobSuites.Api.Models;

namespace JobSuites.Api.Matching;

/// <summary>
/// Computes a match score from a profile and a role, with the reasoning attached.
///
/// Every point awarded is traceable to a specific fact on both sides. That is the
/// whole design constraint: the user is shown a number, and if they cannot see
/// why, the number is noise. Scores are deterministic — the same profile and
/// role always produce the same result, so a score never shifts under the user
/// without the evidence changing too.
/// </summary>
public static class MatchEngine
{
    // Weights sum to 100.
    private const int SkillPoints = 50;
    private const int ExperiencePoints = 25;
    private const int QualificationPoints = 15;
    private const int LocationPoints = 10;

    /// <summary>Role skills extracted from the JD, weighted lower than a CV skill
    /// the user explicitly listed with years attached.</summary>
    private static int Score(List<ProfileSkill> profileSkills, List<string> roleSkills, out List<MatchEvidence> evidence)
    {
        var evidenceList = new List<MatchEvidence>();
        if (profileSkills.Count == 0 || roleSkills.Count == 0)
        {
            evidence = evidenceList;
            return 0;
        }

        var profileByName = profileSkills.ToDictionary(
            s => Normalize(s.Name), s => s, StringComparer.OrdinalIgnoreCase);

        var matched = new List<(string Role, string Profile, bool IsCore)>();

        foreach (var raw in roleSkills)
        {
            var key = Normalize(raw);
            if (key.Length == 0) continue;

            if (profileByName.TryGetValue(key, out var skill))
            {
                matched.Add((raw, skill.Name, skill.IsCore));
                continue;
            }

            // Try a substring match either way, since "react native" and "react"
            // are different skills that candidates list inconsistently.
            var partial = profileByName.FirstOrDefault(
                kv => kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase)
                   || key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));

            if (partial.Value is not null && partial.Key.Length >= 3)
            {
                matched.Add((raw, partial.Value.Name, partial.Value.IsCore));
            }
        }

        if (matched.Count == 0)
        {
            evidence = evidenceList;
            return 0;
        }

        // Coverage of the role's requirements, not of the CV. Scoring against
        // what the job asks for keeps a long CV from beating a well-matched one.
        var coverage = Math.Min(1.0, matched.Count / (double)Math.Max(1, roleSkills.Count));
        var coreBonus = matched.Any(m => m.IsCore) ? 1.15 : 1.0;
        var score = (int)Math.Round(SkillPoints * coverage * coreBonus);

        foreach (var group in matched.GroupBy(m => m.Profile, StringComparer.OrdinalIgnoreCase))
        {
            var roles = group.Select(g => g.Role).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var core = group.Any(g => g.IsCore);
            var years = profileSkills.First(s =>
                string.Equals(s.Name, group.Key, StringComparison.OrdinalIgnoreCase)).Years;

            evidenceList.Add(new MatchEvidence(
                Kind: "skill",
                Label: group.Key,
                Detail: years > 0
                    ? $"You list {years} year{(years == 1 ? "" : "s")} of {group.Key}"
                    : core
                        ? "Listed as a core skill on your CV"
                        : $"Matches the posting's requirement of {string.Join(", ", roles)}",
                Points: 0,
                RolePostingUrl: null));
        }

        evidence = evidenceList;
        return Math.Min(SkillPoints, score);
    }

    private static int ScoreExperience(int? years, Role role, out string? gap)
    {
        gap = null;

        if (years is null)
        {
            // No experience data is not a failure, it is an unknown. Award half
            // and say so, rather than scoring a CV we simply could not read.
            gap = "We could not read your years of experience from the CV";
            return ExperiencePoints / 2;
        }

        if (role.MinYears is not null && years.Value < role.MinYears.Value)
        {
            gap = $"This role asks for {role.MinYears}+ years; your CV shows {years.Value}";
            // Below the floor the score is proportional, not zero — being one
            // year under is not the same as being ten under.
            var ratio = (double)years.Value / role.MinYears.Value;
            return (int)Math.Round(ExperiencePoints * ratio * 0.5);
        }

        if (role.MaxYears is not null && years.Value > role.MaxYears.Value + 3)
        {
            gap = $"This role tops out at {role.MaxYears} years; you show {years.Value}";
            return (int)Math.Round(ExperiencePoints * 0.7);
        }

        return ExperiencePoints;
    }

    private static int ScoreQualification(ProfileExperience[] experiences, Role role, out string? gap)
    {
        gap = null;

        if (role.Qualification is not { Length: > 2 } requirement)
        {
            return QualificationPoints;
        }

        var haystack = string.Join(" ",
            role.Qualification.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var profileText = string.Join(" ",
            experiences.Select(e => $"{e.Title} {e.Highlights} {e.Company}")).ToLowerInvariant();

        var tokens = requirement
            .Replace(",", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', '(', ')').ToLowerInvariant())
            .Where(t => t.Length > 3)
            .Distinct()
            .ToList();

        if (tokens.Count == 0) return QualificationPoints;

        var hits = tokens.Count(t => profileText.Contains(t, StringComparison.OrdinalIgnoreCase));
        var ratio = hits / (double)tokens.Count;

        if (hits == 0)
        {
            gap = $"Asks for {requirement}; not visible on your CV";
        }

        return (int)Math.Round(QualificationPoints * ratio);
    }

    private static int ScoreLocation(CandidateProfile? profile, Role role, out string? gap)
    {
        gap = null;

        if (role.States.Count == 0) return LocationPoints;

        var userLocation = profile?.Location;
        if (string.IsNullOrWhiteSpace(userLocation))
        {
            gap = "No location on your CV, so we could not check the work location";
            return LocationPoints / 2;
        }

        var normalized = Normalize(userLocation);
        var hit = role.States.FirstOrDefault(s => Normalize(s) == normalized);

        if (hit is null)
        {
            gap = $"Based in {userLocation}; this role is in {string.Join(", ", role.States.Take(3))}";
            return 0;
        }

        return LocationPoints;
    }

    public static RoleMatch Compute(CandidateProfile profile, Role role, string? primaryPostingUrl)
    {
        var evidence = new List<MatchEvidence>();
        var gaps = new List<string>();

        var skillScore = Score(profile.Skills, role.Skills, out var skillEvidence);
        evidence.AddRange(skillEvidence);

        var expScore = ScoreExperience(profile.YearsExperience, role, out var expGap);
        if (expGap is not null) gaps.Add(expGap);

        var qualScore = ScoreQualification([.. profile.Experiences], role, out var qualGap);
        if (qualGap is not null) gaps.Add(qualGap);

        var locScore = ScoreLocation(profile, role, out var locGap);
        if (locGap is not null) gaps.Add(locGap);

        var total = Math.Clamp(skillScore + expScore + qualScore + locScore, 0, 100);

        // Attach the posting URL to each piece of evidence so every claim in the
        // UI can be checked against the source.
        evidence = evidence
            .Select(e => e with { RolePostingUrl = primaryPostingUrl })
            .ToList();

        return new RoleMatch
        {
            UserId = profile.UserId,
            JobId = role.Id,
            ProfileHash = JobSuites.Api.Services.ProfileMatchHash.Compute(profile),
            Score = total,
            Tier = TierFor(total),
            Evidence = evidence,
            Gaps = gaps,
            ComputedAt = DateTimeOffset.UtcNow,
        };
    }

    public static string TierFor(int score) => score switch
    {
        >= 75 => "Strong",
        >= 55 => "Good",
        >= 35 => "Possible",
        _ => "Stretch",
    };

    /// <summary>Strips punctuation and casing so "C#" and "C Sharp" style variants
    /// collapse, and trailing punctuation does not block a match.</summary>
    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\w\+#\s]", " ").Trim();
}
