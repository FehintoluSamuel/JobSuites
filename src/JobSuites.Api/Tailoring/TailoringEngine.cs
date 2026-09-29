using JobSuites.Api.Matching;
using JobSuites.Api.Models;

namespace JobSuites.Api.Tailoring;

/// <summary>
/// Assembles a CV for one role out of facts the candidate has already supplied.
///
/// The constraint from docs/PRODUCT.md §6.1 is that this may SELECT, ORDER and
/// REFRAME existing true facts, and may do nothing else. That is enforced
/// structurally rather than by asking nicely: every string in the output is
/// either a verbatim field from <see cref="CandidateProfile"/> or a template
/// wrapper around one. The words this class can introduce are a fixed, finite set,
/// and any role-specific term it inserts is one it has already confirmed the
/// candidate has.
///
/// This class is the deterministic assembly step. An optional LLM pass
/// (JobSuites.Api.Services.Llm.LlmTailoringPass) may refine the prose afterwards,
/// but every fact line still originates here, from the closed profile set.
/// </summary>
public static class TailoringEngine
{
    private static readonly string[] SectionOrder =
    [
        "Target", "Summary", "Experience", "Skills", "Education", "Certifications", "Languages",
    ];

    public static TailoredBuild Build(CandidateProfile profile, Role role)
    {
        var requirements = RoleRequirements.Extract(role);
        var facts = FactSet.From(profile);
        var diff = new List<TailoredChange>();
        var factRefs = new List<string>();
        var sections = new List<TailoredSection>();

        // ---- Coverage -------------------------------------------------------
        // Resolved before assembly: which requirements can this CV actually
        // speak to, and which are gaps. The gaps are reported, never filled.
        var coverage = requirements
            .Select(r =>
            {
                var support = facts.FindSupport(r);
                if (support is not null) factRefs.Add(support.Ref);
                return new RequirementCoverage(
                    Requirement: r.Text,
                    MustHave: r.MustHave,
                    Covered: support is not null,
                    SupportingFact: support?.Ref);
            })
            .ToList();

        var focusSkills = requirements
            .Where(r => r.Category == "skill")
            .Select(r => (Requirement: r, Normalized: RoleRequirements.Normalize(r.Text)))
            .Where(x => facts.SkillByNormalized.ContainsKey(x.Normalized))
            .Select(x => facts.SkillByNormalized[x.Normalized].Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- Ordering -------------------------------------------------------
        // Experience is reordered so the roles that answer this posting lead.
        // Relevance is counted from the role's own skills, so the ordering is
        // derived from the posting rather than from a generic "most recent
        // first" rule that would ignore what the role asked for.
        var orderedExperience = profile.Experiences
            .Select(e => new
            {
                Experience = e,
                Relevance = CountRelevantSkills(e, role.Skills),
                Ref = FactRef.For("experience", e.Company, e.Title),
            })
            .OrderByDescending(x => x.Relevance)
            .ThenByDescending(x => x.Experience.IsCurrent)
            .ThenBy(x => x.Experience.StartDate, StringComparer.Ordinal)
            .ToList();

        var masterOrder = profile.Experiences
            .Select(e => FactRef.For("experience", e.Company, e.Title))
            .ToList();

        if (masterOrder.Count > 1 && !masterOrder.SequenceEqual(orderedExperience.Select(x => x.Ref)))
        {
            diff.Add(new TailoredChange(
                Kind: "reordered",
                Detail: "Experience reordered so the roles matching this posting come first. " +
                        $"CV order: {string.Join(", ", masterOrder)}. This version: " +
                        $"{string.Join(", ", orderedExperience.Select(x => x.Ref))}."));
        }

        // ---- Sections -------------------------------------------------------
        sections.Add(new TailoredSection(
            Heading: "Target",
            // Job facts, not claims about the candidate. The fabrication check is
            // told to expect these two strings.
            Body: $"{role.Title} — {role.Company}",
            Source: "role"));

        if (!string.IsNullOrWhiteSpace(profile.FullName) ||
            !string.IsNullOrWhiteSpace(profile.Headline))
        {
            var contact = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.FullName)) contact.Add(profile.FullName);
            if (!string.IsNullOrWhiteSpace(profile.Headline)) contact.Add(profile.Headline);
            if (!string.IsNullOrWhiteSpace(profile.Location)) contact.Add(profile.Location);
            if (!string.IsNullOrWhiteSpace(profile.Email)) contact.Add(profile.Email);
            if (!string.IsNullOrWhiteSpace(profile.Phone)) contact.Add(profile.Phone);

            sections.Add(new TailoredSection(
                Heading: "Contact",
                Body: string.Join(" · ", contact),
                Source: "profile"));
        }

        if (!string.IsNullOrWhiteSpace(profile.Summary))
        {
            var summary = profile.Summary.Trim();

            // The only words this engine adds to a candidate's own prose. Each one
            // is a role skill already confirmed present in the profile, so the
            // sentence cannot introduce a capability the CV does not support.
            if (focusSkills.Count > 0)
            {
                summary = $"{summary} Focus for this role: {string.Join(", ", focusSkills.Take(4))}.";
                diff.Add(new TailoredChange(
                    Kind: "reframed",
                    Detail: $"Summary lead-in added naming {string.Join(", ", focusSkills.Take(4))}. " +
                            "Every one of those is already a listed skill on your CV.",
                    FactRef: string.Join(", ", focusSkills.Take(4))));
            }

            sections.Add(new TailoredSection("Summary", summary, "profile"));
        }

        if (orderedExperience.Count > 0)
        {
            var blocks = new List<string>();
            foreach (var item in orderedExperience)
            {
                var e = item.Experience;
                factRefs.Add(item.Ref);
                var header = string.IsNullOrWhiteSpace(e.StartDate)
                    ? $"{e.Title}, {e.Company}"
                    : $"{e.Title}, {e.Company} ({e.StartDate} – {e.EndDate ?? "Present"})";

                var bullets = (e.Highlights ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(h => h.Length > 0)
                    .Select(h => $"  • {h}")
                    .ToList();

                blocks.Add(bullets.Count == 0
                    ? header
                    : $"{header}\n{string.Join("\n", bullets)}");
            }

            sections.Add(new TailoredSection(
                "Experience", string.Join("\n\n", blocks), "profile"));
        }

        if (profile.Skills.Count > 0)
        {
            // Same skills, relevance order. Nothing is added or removed, so this
            // cannot invent a capability; it can only change what leads.
            var promoted = focusSkills
                .Where(f => !string.Equals(f, profile.Headline, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rest = profile.Skills
                .Select(s => s.Name)
                .Where(n => !promoted.Contains(n, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var body = new List<string>();
            if (promoted.Count > 0) body.Add($"Core: {string.Join(", ", promoted)}");
            if (rest.Count > 0) body.Add($"Supporting: {string.Join(", ", rest)}");

            if (promoted.Count > 0)
            {
                diff.Add(new TailoredChange(
                    Kind: "promoted",
                    Detail: $"{promoted.Count} role skill(s) moved to the Core line: " +
                            $"{string.Join(", ", promoted)}. All already on your CV.",
                    FactRef: string.Join(", ", promoted)));
            }

            sections.Add(new TailoredSection("Skills", string.Join("\n", body), "profile"));
        }

        if (profile.Education.Count > 0)
        {
            var lines = profile.Education.Select(e =>
            {
                var degree = string.Join(" ", new[] { e.Degree, e.FieldOfStudy }
                    .Where(p => !string.IsNullOrWhiteSpace(p)));
                var school = e.School ?? string.Empty;
                var years = e.EndYear is { } y ? $" ({y})" : string.Empty;
                return string.Join(", ", new[] { degree, school }.Where(p => p.Length > 0)) + years;
            }).Where(l => l.Length > 0).ToList();

            if (lines.Count > 0)
                sections.Add(new TailoredSection("Education", string.Join("\n", lines), "profile"));
        }

        if (profile.Certifications.Count > 0)
        {
            var lines = profile.Certifications
                .Select(c => string.Join(", ", new[]
                {
                    c.Name,
                    c.Issuer,
                    c.Year?.ToString(),
                }.Where(p => !string.IsNullOrWhiteSpace(p))))
                .ToList();
            if (lines.Count > 0)
                sections.Add(new TailoredSection("Certifications", string.Join("\n", lines), "profile"));
        }

        if (profile.Languages.Count > 0)
        {
            var lines = profile.Languages
                .Select(l => $"{l.Name} — {l.Proficiency}")
                .ToList();
            if (lines.Count > 0)
                sections.Add(new TailoredSection("Languages", string.Join("\n", lines), "profile"));
        }

        var uncoveredMust = coverage.Where(c => c.MustHave && !c.Covered).ToList();
        if (uncoveredMust.Count > 0)
        {
            // Reported, not hidden and not invented — docs/PRODUCT.md §6.3. This
            // is written into the diff so it reaches the user as part of review
            // rather than as something they have to go looking for.
            diff.Add(new TailoredChange(
                Kind: "gap",
                Detail: "This role asks for " +
                        $"{string.Join("; ", uncoveredMust.Select(u => u.Requirement))}. " +
                        "No supporting fact was found on your CV, so nothing was written " +
                        "about them. Add the experience, or leave it out."));
        }

        // Fixed order regardless of assembly order, so two runs over the same
        // profile produce byte-identical output.
        var ordered = sections
            .OrderBy(s => Array.IndexOf(SectionOrder, s.Heading))
            .ToList();

        return new TailoredBuild(ordered, diff, coverage, factRefs.Distinct().ToList());
    }

    private static int CountRelevantSkills(ProfileExperience e, List<string> roleSkills)
    {
        if (roleSkills.Count == 0) return 0;
        var haystack = RoleRequirements.Normalize($"{e.Title} {e.Highlights} {e.Company}");
        return roleSkills.Count(s => haystack.Contains(RoleRequirements.Normalize(s), StringComparison.Ordinal));
    }

    /// <summary>
    /// Requirements paired with whether the profile can support them.
    ///
    /// Interview prep classifies gaps through this rather than through its own
    /// lookup, so "uncovered" means the same thing in a tailored CV's coverage
    /// panel and in the questions generated from it. Two independent definitions
    /// would let the product tell a user a requirement was covered and then ask
    /// them to defend it in an interview.
    /// </summary>
    public static List<(RoleRequirement Requirement, bool Supported)> ClassifyForPrep(
        CandidateProfile profile, Role role)
    {
        var facts = FactSet.From(profile);
        return RoleRequirements.Extract(role)
            .Select(r => (Requirement: r, Supported: facts.FindSupport(r) is not null))
            .ToList();
    }

    public static string RenderText(IEnumerable<TailoredSection> sections) =>
        string.Join("\n\n", sections.Select(s => $"{s.Heading.ToUpperInvariant()}\n{s.Body}"));

    /// <summary>
    /// Coverage for one profile against one role, without assembling a document.
    ///
    /// Exposed so interview prep classifies gaps through exactly the same code
    /// path tailoring uses. If the two read different rules, a tailored CV could
    /// report a requirement as covered while prep generated a question about it,
    /// and the user would be told two contradictory things about the same role.
    /// </summary>
    public static List<RequirementCoverage> BuildCoverageForReview(CandidateProfile profile, Role role)
    {
        var facts = FactSet.From(profile);

        return RoleRequirements.Extract(role)
            .Select(r =>
            {
                var support = facts.FindSupport(r);
                return new RequirementCoverage(
                    Requirement: r.Text,
                    MustHave: r.MustHave,
                    Covered: support is not null,
                    SupportingFact: support?.Description);
            })
            .ToList();
    }
}

public record TailoredBuild(
    List<TailoredSection> Document,
    List<TailoredChange> Diff,
    List<RequirementCoverage> Coverage,
    List<string> FactRefs);

/// <summary>Stable identifier for a profile fact, so a line in the document can
/// be traced back to the field it came from.</summary>
public static class FactRef
{
    public static string For(string kind, params string?[] parts)
    {
        var joined = string.Join(" · ", parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));
        return $"{kind}:{joined}";
    }
}

/// <summary>
/// The candidate's facts, indexed for requirement matching. Built once per
/// tailoring run so coverage, ordering and the fabrication corpus all read from
/// the same set — if they read from different sets the document could claim a
/// skill the coverage report says is missing.
/// </summary>
internal sealed class FactSet
{
    private readonly CandidateProfile _profile;
    private readonly List<ProfileEducation> _education;

    public Dictionary<string, ProfileSkill> SkillByNormalized { get; } = new(StringComparer.Ordinal);

    private FactSet(CandidateProfile profile)
    {
        _profile = profile;
        _education = profile.Education;

        foreach (var skill in profile.Skills)
        {
            var key = string.IsNullOrWhiteSpace(skill.Normalized)
                ? RoleRequirements.Normalize(skill.Name)
                : skill.Normalized;

            if (key.Length > 0) SkillByNormalized[key] = skill;
        }
    }

    public static FactSet From(CandidateProfile profile) => new(profile);

    /// <summary>The profile fact that answers a requirement, or null. Returning
    /// the fact rather than a bool is what lets the gap surface name exactly what
    /// was and was not found.</summary>
    public Support? FindSupport(RoleRequirement requirement) => requirement.Category switch
    {
        "skill" => SupportSkill(requirement),
        "education" => SupportEducation(requirement),
        "experience" => SupportExperience(requirement),
        _ => null,
    };

    private Support? SupportSkill(RoleRequirement requirement)
    {
        var key = RoleRequirements.Normalize(requirement.Text);
        if (key.Length == 0) return null;

        if (SkillByNormalized.TryGetValue(key, out var exact))
        {
            return new Support(FactRef.For("skill", exact.Name),
                $"Listed skill: {exact.Name}" + Years(exact.Years));
        }

        // Near-misses: "react native" against a listed "react", and the reverse.
        // The same leniency the matcher applies, so a fact the score credits is
        // a fact tailoring will also use.
        foreach (var (normalized, skill) in SkillByNormalized)
        {
            if (normalized.Length < 3) continue;
            if (normalized.Contains(key, StringComparison.Ordinal) ||
                key.Contains(normalized, StringComparison.Ordinal))
            {
                return new Support(FactRef.For("skill", skill.Name),
                    $"Listed skill: {skill.Name} covers {requirement.Text}" + Years(skill.Years));
            }
        }

        return null;
    }

    private static string Years(int years) =>
        years > 0 ? $" ({years} yr{(years == 1 ? "" : "s")})" : string.Empty;

    private Support? SupportEducation(RoleRequirement requirement)
    {
        var tokens = requirement.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => RoleRequirements.Normalize(t))
            .Where(t => t.Length > 3)
            .Distinct()
            .ToList();

        if (tokens.Count == 0) return null;

        foreach (var e in _education)
        {
            var haystack = RoleRequirements.Normalize($"{e.Degree} {e.FieldOfStudy} {e.School}");
            if (tokens.Any(t => haystack.Contains(t, StringComparison.Ordinal)))
            {
                var field = string.IsNullOrWhiteSpace(e.FieldOfStudy) ? "" : $" {e.FieldOfStudy}";
                return new Support(
                    FactRef.For("education", e.School, e.Degree),
                    $"{e.Degree}{field} at {e.School}");
            }
        }

        return null;
    }

    private Support? SupportExperience(RoleRequirement requirement)
    {
        if (requirement.YearsMin is not { } needed) return null;

        // An unknown number is not a pass. Claiming coverage we cannot show would
        // be exactly the kind of quiet overstatement this product is built to avoid.
        if (_profile.YearsExperience is not { } have) return null;

        return have >= needed
            ? new Support(
                FactRef.For("experience", "total years"),
                $"Your CV shows {have} years against {needed}+ asked for")
            : null;
    }
}

public record Support(string Ref, string Description);
