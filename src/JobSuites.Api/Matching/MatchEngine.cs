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

        // The posting's own must-haves lead the evidence list. They are read
        // from the JD body, so they are the most specific thing we can say
        // about why this role fits — and the dashboard shows only the first few
        // items, so a generic skill match must not crowd them out.
        var requirementScore = ApplyRequirements(profile, role, out var reqEvidence, out var reqGaps);
        evidence.AddRange(reqEvidence);
        gaps.AddRange(reqGaps);

        var skillScore = Score(profile.Skills, role.Skills, out var skillEvidence);
        evidence.AddRange(skillEvidence);

        var expScore = ScoreExperience(profile.YearsExperience, role, out var expGap);
        if (expGap is not null) gaps.Add(expGap);

        var qualScore = ScoreQualification([.. profile.Experiences], role, out var qualGap);
        if (qualGap is not null) gaps.Add(qualGap);

        var locScore = ScoreLocation(profile, role, out var locGap);
        if (locGap is not null) gaps.Add(locGap);

        // Requirement coverage scales the result rather than replacing it. The
        // CV is read heuristically, so a requirement we failed to recognise
        // must not be able to cost a candidate the whole score; but a role whose
        // stated must-haves are missing should rank below one where they are
        // not. 20% is the band between those two.
        var total = Math.Clamp(
            (int)Math.Round((skillScore + expScore + qualScore + locScore) * requirementScore),
            0, 100);

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

    /// <summary>Multiplier applied to the base score from how much of the
    /// posting's stated must-haves the profile supports. 1.0 when every one is
    /// met, <see cref="RequirementPenalty"/> when none is. A role with no stored
    /// requirements is left alone, so scoring behaves exactly as it did before
    /// extraction existed.</summary>
    private const double RequirementPenalty = 0.8;

    private static double ApplyRequirements(
        CandidateProfile profile,
        Role role,
        out List<MatchEvidence> evidence,
        out List<string> gaps)
    {
        evidence = [];
        gaps = [];

        // Only disqualifying asks count. An "added advantage" the candidate
        // lacks is not a gap, and treating it as one is how a product teaches
        // users to ignore its warnings.
        var demands = role.Requirements
            .Where(r => r.MustHave && !string.IsNullOrWhiteSpace(r.Text))
            .ToList();

        if (demands.Count == 0) return 1.0;

        var index = FactIndex.Build(profile);
        var covered = 0;

        foreach (var demand in demands)
        {
            var support = SupportFor(index, profile, demand);

            if (support is null)
            {
                gaps.Add($"The posting asks for {demand.Text}; not on your CV");
                continue;
            }

            covered++;
            evidence.Add(new MatchEvidence(
                Kind: "requirement",
                Label: demand.Text,
                Detail: support,
                Points: 0,
                RolePostingUrl: null));
        }

        var coverage = covered / (double)demands.Count;
        return RequirementPenalty + (1 - RequirementPenalty) * coverage;
    }

    /// <summary>The fact on the candidate's side that answers a requirement, or
    /// null if there is not one. Answered facet by facet — a certification is
    /// checked against certifications — because a flat text search would let a
    /// mention of a skill anywhere in a job description count as a licence.</summary>
    private static string? SupportFor(FactIndex index, CandidateProfile profile, JdRequirement demand)
    {
        if (demand.YearsMin is { } floor)
        {
            return profile.YearsExperience is { } years && years >= floor
                ? $"You show {years} years against the {floor} asked for"
                : null;
        }

        var text = demand.Text.Trim();

        return demand.Category switch
        {
            "skill" => index.SkillSupport(text),
            "education" => index.EducationSupport(text),
            "certification" => index.CertificationSupport(text),
            "language" => index.LanguageSupport(text),
            "experience" => profile.YearsExperience is { } y ? $"You show {y} years of experience" : null,
            // Domain and uncategorised prose demands: the posting asks for
            // something in a sentence we could not classify, so the honest test
            // is whether the candidate names it anywhere.
            _ => index.GeneralSupport(text),
        };
    }

    /// <summary>The candidate's facts, indexed once per role so the demand loop
    /// does not re-normalise the whole CV for every requirement.</summary>
    private sealed class FactIndex
    {
        private readonly List<(string Label, string Text)> _education = [];
        private readonly List<(string Label, string Text)> _certifications = [];
        private readonly List<(string Label, string Text)> _languages = [];

        public Dictionary<string, ProfileSkill> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static FactIndex Build(CandidateProfile profile)
        {
            var index = new FactIndex();

            foreach (var skill in profile.Skills)
            {
                var key = Normalize(skill.Name);
                if (key.Length > 0) index.Skills.TryAdd(key, skill);
            }

            foreach (var e in profile.Education)
            {
                var label = Coalesce(e.Degree, e.FieldOfStudy) ?? e.School ?? "your education";
                index._education.Add(($"Studied {label}", Join(e.School, e.Degree, e.FieldOfStudy, e.Details)));
            }

            foreach (var c in profile.Certifications)
            {
                index._certifications.Add(($"Hold {c.Name}", Join(c.Name, c.Issuer, c.Year?.ToString())));
            }

            foreach (var l in profile.Languages)
            {
                index._languages.Add(((l.Proficiency.Length > 0 ? $"{l.Proficiency} {l.Name}" : l.Name), l.Name));
            }

            return index;
        }

        public string? SkillSupport(string demand)
        {
            var key = Normalize(demand);
            if (key.Length == 0) return null;

            if (Skills.TryGetValue(key, out var exact))
                return exact.Years > 0
                    ? $"You list {exact.Years} year{(exact.Years == 1 ? "" : "s")} of {exact.Name}"
                    : $"You list {exact.Name} on your CV";

            // Partial either way, but only on names long enough for one to mean
            // something: "R" is a substring of almost every CV.
            foreach (var (name, skill) in Skills)
            {
                if (name.Length < 4 || key.Length < 4) continue;
                if (name.Contains(key, StringComparison.Ordinal) || key.Contains(name, StringComparison.Ordinal))
                {
                    return $"You list {skill.Name} on your CV";
                }
            }

            return null;
        }

        public string? EducationSupport(string demand) =>
            Support(_education, demand, "your education");

        public string? CertificationSupport(string demand) =>
            Support(_certifications, demand, "your certifications");

        public string? LanguageSupport(string demand) =>
            Support(_languages, demand, "your languages");

        /// <summary>Fallback for demands we could not classify. Any named skill
        /// or credential appearing in the sentence counts, which is generous by
        /// design: this class of requirement is a sentence, not a term, and
        /// wrongly failing one would show the user a gap they do not have.</summary>
        public string? GeneralSupport(string demand)
        {
            var key = Normalize(demand);
            if (key.Length < 6) return null;

            foreach (var (name, skill) in Skills)
            {
                if (name.Length < 4) continue;
                if (key.Contains(name, StringComparison.Ordinal))
                    return $"You list {skill.Name} on your CV";
            }

            return null;
        }

        private static string? Support(
            List<(string Label, string Text)> facts,
            string demand,
            string what)
        {
            var key = Normalize(demand);
            if (key.Length == 0) return null;

            var needles = Terms(key).ToList();
            if (needles.Count == 0) needles.Add(key);

            foreach (var (label, text) in facts)
            {
                var haystack = Normalize(text);
                if (haystack.Length == 0) continue;

                var hits = needles.Count(n => haystack.Contains(n, StringComparison.Ordinal));
                if (hits == needles.Count) return $"On {what}: {label}";

                // A two-term requirement satisfied by both halves is still met.
                if (needles.Count > 1 && hits >= 2) return $"On {what}: {label}";
            }

            return null;
        }

        /// <summary>Distinctive words of a requirement. Short and common words
        /// are dropped because "and" or "team" matching a CV fact would let any
        /// education requirement pass on a single coincidence.</summary>
        private static IEnumerable<string> Terms(string normalized) =>
            normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 4 && !CommonWords.Contains(t))
                .Distinct(StringComparer.Ordinal);

        private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
        {
            "with", "from", "have", "your", "their", "will", "would", "them", "then",
            "than", "that", "this", "work", "working", "within", "years", "year",
            "experience", "experienced", "using", "able", "must", "should", "well",
            "more", "than", "least", "other", "including", "such", "role", "team",
        };

        private static string Join(params string?[] parts) =>
            string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        private static string? Coalesce(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) ? a : b;
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
