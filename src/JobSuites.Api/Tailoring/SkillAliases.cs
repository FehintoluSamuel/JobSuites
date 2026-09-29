using System.Text.RegularExpressions;

namespace JobSuites.Api.Tailoring;

/// <summary>
/// Heuristic support for the "minor normalization" contract: a role skill may be
/// named in a tailored document when the CV clearly implies it through a trusted
/// alias ("spreadsheets" → "Excel", "ML" → "AI / machine learning").
///
/// Deliberately a small, auditable table rather than a model judgement. Every
/// key maps to a fixed set of phrases that a hiring auditor would accept as
/// evidence of the skill. Anything not in the table falls back to an exact
/// word-boundary match of the skill's own name, so this widens what passes
/// without letting unrelated claims through.
/// </summary>
public static class SkillAliases
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Spreadsheets
        ["excel"] = ["spreadsheet", "spreadsheets", "google sheets", "microsoft excel"],
        ["microsoft excel"] = ["excel", "spreadsheet", "spreadsheets", "google sheets"],
        // AI / ML family
        ["ai"] = ["artificial intelligence", "machine learning", "deep learning", "ml model", "computer vision", "neural network"],
        ["machine learning"] = ["ml", "deep learning", "classification model", "model training", "tensorflow", "sklearn", "ai"],
        ["artificial intelligence"] = ["ai", "machine learning", "machine-learning", "deep learning"],
        // Data analysis
        ["data analysis"] = ["data analytics", "analytics", "data analyst", "statistical analysis", "data cleaning", "dashboarding", "data visualization"],
        ["data analytics"] = ["data analysis", "analytics", "data analyst", "data visualization"],
        ["analytics"] = ["data analysis", "data analytics", "analytics dashboard"],
        // Communication / reporting
        ["communication"] = ["communicated", "presentation", "presentations", "reporting", "report", "written", "liaison", "stakeholder", "stakeholders"],
        ["communication skills"] = ["communication", "presentation", "presentations", "reporting", "stakeholder", "stakeholders", "liaison"],
        // Project / delivery
        ["project management"] = ["project manager", "sprint planning", "scrum", "agile", "delivery", "roadmap", "stakeholder management"],
        ["agile"] = ["scrum", "sprint", "sprints", "sprint planning", "kanban", "ci/cd"],
        ["scrum"] = ["scrum master", "sprint", "sprints", "sprint planning", "agile"],
        // Databases
        ["sql"] = ["mysql", "postgresql", "postgres", "sql server", "mssql", "tsql", "t-sql", "relational database", "database query", "querying", "crud"],
        ["database management"] = ["database", "databases", "mysql", "postgresql", "postgres", "sql server", "mssql", "mongodb", "relational database"],
        // Cloud / ops
        ["cloud computing"] = ["cloud", "aws", "azure", "gcp", "azure container apps", "azure container instances", "kubernetes", "docker"],
        ["devops"] = ["ci/cd", "github actions", "azure devops", "pipeline", "pipelines", "docker", "kubernetes", "deployment"],
        ["power bi"] = ["microsoft power bi", "powerbi", "bi dashboard", "dashboard", "dax", "power query"],
        // QMS / quality
        ["quality management"] = ["quality assurance", "quality control", "iso", "qms", "compliance", "inspection"],
        ["quality control"] = ["quality assurance", "qc", "iso", "compliance", "inspection", "inspection-time"],
        ["quality assurance"] = ["quality control", "qa", "testing", "test cases", "defect", "defects"],
        // Testing / engineering support
        ["testing"] = ["test", "tests", "testing", "qa", "defect", "defects", "unit test", "integration test"],
        // Procurement
        ["procurement"] = ["purchasing", "vendor", "vendors", "supplier", "suppliers", "stock control", "inventory"],
        // HSE
        ["hse"] = ["health, safety", "health and safety", "safety officer", "hazard", "hazards", "environmental"],
        ["safety management"] = ["hse", "health, safety", "health and safety", "safety", "hazard", "hazards"],
        // Sales / customer
        ["customer service"] = ["customer support", "customer-facing", "client-facing", "client support", "helpdesk", "help desk"],
    };

    /// <summary>
    /// Every phrase that counts as evidence of the skill, including the skill's
    /// own name, normalised to a space-folded lowercase for corpus matching.
    /// </summary>
    public static IReadOnlyList<string> Variants(string skill)
    {
        var trimmed = skill.Trim();
        if (trimmed.Length == 0) return [];

        var basePhrase = NormalizePhrase(trimmed);
        var result = new List<string> { basePhrase };

        if (Aliases.TryGetValue(trimmed, out var aliases))
        {
            foreach (var a in aliases)
            {
                var n = NormalizePhrase(a);
                if (!result.Contains(n, StringComparer.Ordinal)) result.Add(n);
            }
        }

        // "data analysis" should also imply "analyze"/"analyzed" inflections of the
        // core noun, matching the vague wording CVs actually use.
        var core = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (core is { Length: > 3 })
        {
            var inflected = NormalizePhrase(core) + "ed";   // "analyze" → "analyzed"
            var plural = NormalizePhrase(core) + "s";       // "report" → "reports"
            if (!result.Contains(inflected, StringComparer.Ordinal)) result.Add(inflected);
            if (!result.Contains(plural, StringComparer.Ordinal)) result.Add(plural);
        }

        return result;
    }

    /// <summary>
    /// True when the raw text mentions any variant of the skill as a standalone
    /// sequence of words. Operates on the untouched text (case-folded, spaces
    /// preserved) so "AI" matches its own token without matching inside "API".
    /// </summary>
    public static bool MentionedIn(string text, string skill)
    {
        var folded = text.ToLowerInvariant();
        foreach (var variant in Variants(skill))
        {
            if (variant.Length == 0) continue;
            if (Regex.IsMatch(folded, PhrasePattern(variant), RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    /// <summary>Lowercase, single spaces, punctuation stripped from the middle so
    /// "machine-learning" and "machine learning" agree.</summary>
    private static string NormalizePhrase(string phrase) =>
        Regex.Replace(phrase.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    /// <summary>Word-boundary regex for a space-folded phrase: every word is
    /// literal and must be wrapped by non-word characters.</summary>
    private static string PhrasePattern(string normalizedPhrase)
    {
        var words = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var body = string.Join(@"[\s\-]+", words.Select(Regex.Escape));
        return $@"\b{body}\b";
    }
}