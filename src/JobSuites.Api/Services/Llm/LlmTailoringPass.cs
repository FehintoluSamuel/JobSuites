using System.Text.Json;
using JobSuites.Api.Models;
using JobSuites.Api.Tailoring;

namespace JobSuites.Api.Services.Llm;

/// <summary>
/// LLM-assisted tailoring pass. Runs on top of the deterministic engine (which
/// owns the section inventory, the coverage report and the fact provenance) and
/// regenerates the document's prose for a specific posting.
///
/// To generate with it, the model is given the posting (title, company,
/// requirements and per-requirement coverage) and the candidate's actual CV
/// corpus — raw text plus the structured facts — and told to write a targeted
/// CV from that material only. Rewriting is free: wording, ordering and emphasis
/// are the model's, including synonyms for a skill the CV clearly supports
/// ("spreadsheets" → "Excel"). What stays closed-set is the facts the document
/// can claim: employers, titles, dates, degrees, certifications, and figures.
///
/// The AI result is merged back into the deterministic build's exact section
/// set, so a model that adds or drops a section cannot change the document's
/// content inventory. The caller then runs <see cref="FabricationCheck"/> over
/// the merged result; a rewrite that trips the check is discarded — the stored
/// document stays the deterministic build — but is kept alongside it as a
/// diagnosis, so the rejection is something the user can read rather than a
/// verdict with nothing behind it.
/// </summary>
public sealed class LlmTailoringPass
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILlmClient _llm;
    private readonly ILogger<LlmTailoringPass> _log;

    public LlmTailoringPass(ILlmClient llm, ILogger<LlmTailoringPass> log)
    {
        _llm = llm;
        _log = log;
    }

    private const string SystemPrompt =
        """
        You are a senior CV writer specialising in tailoring real CVs to a single
        job posting. You write in the candidate's voice and from their material
        only.

        Rewriting is free: you may reword, restructure, condense and re-emphasise
        anything, and vary the wording as much as a real writer would. Where the
        CV clearly supports a skill through related wording, you may name the
        posting's term for it ("spreadsheets" may become "Excel", "ML" may become
        "AI / machine learning").

        Fabricating is a disqualifying failure, and it means inventing a FACT:
        an employer, job title, date, degree, certification, person, or any
        figure. Those must be taken verbatim from the CV — when you name one, use
        exactly the wording present there. Never introduce an employer, school,
        credential, number or metric the CV does not contain.

        Keep the section headings exactly as provided. The "Target" line must stay
        exactly as provided. Do not add sections and do not drop any.

        Return ONLY a JSON array of {"heading": string, "body": string} with one
        entry per section, bodies in plain text with newlines between items. No
        markdown fences, no commentary.
        """;

    public async Task<List<TailoredSection>?> TryRewriteAsync(
        CandidateProfile profile,
        Role role,
        TailoredBuild build,
        CancellationToken ct)
    {
        if (!_llm.Enabled) return null;
        if (build.Document.Count == 0) return null;

        var prompt = BuildPrompt(profile, role, build);
        var content = await _llm.CompleteAsync(SystemPrompt, prompt, ct);
        if (content is null) return null;

        var json = StripFences(content);
        List<RewrittenSection>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<RewrittenSection>>(json, Json);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "LLM tailoring returned invalid JSON.");
            return null;
        }

        if (parsed is null || parsed.Count == 0) return null;

        var byHeading = parsed
            .Where(s => !string.IsNullOrWhiteSpace(s.Heading))
            .GroupBy(s => s.Heading!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(x => x.Body?.Trim()).Where(b => !string.IsNullOrWhiteSpace(b))),
                StringComparer.OrdinalIgnoreCase);

        // The deterministic build owns the section set. Merge the AI prose back
        // into the same headings so a model that drops or adds a section cannot
        // change the document's content inventory. Target is job-facing, so the
        // deterministic line wins there too.
        var merged = build.Document
            .Select(section =>
            {
                if (string.Equals(section.Heading, "Target", StringComparison.OrdinalIgnoreCase))
                    return section;

                var body = byHeading.GetValueOrDefault(section.Heading);
                return string.IsNullOrWhiteSpace(body)
                    ? section
                    : new TailoredSection(section.Heading, body ?? section.Body, section.Source);
            })
            .ToList();

        // A rewrite that cannot be merged at all (all bands empty) is a failed
        // call, not a silent reversion to the original CV.
        var rewrittenBands = merged.Count(s =>
            byHeading.ContainsKey(s.Heading) && !string.IsNullOrWhiteSpace(byHeading[s.Heading]));

        if (rewrittenBands == 0)
        {
            _log.LogWarning("LLM tailoring returned nothing usable for any section.");
            return null;
        }

        return merged;
    }

    private static string BuildPrompt(CandidateProfile profile, Role role, TailoredBuild build)
    {
        var requirements = string.Join("; ", build.Coverage.Select(c => c.Requirement));
        var coverage = build.Coverage
            .Select(c => c.Covered ? $"[covered] {c.Requirement}" : $"[not on CV] {c.Requirement}");

        var roleFacts = new List<string> { role.Title, role.Company };
        if (!string.IsNullOrWhiteSpace(role.Field)) roleFacts.Add(role.Field);
        if (!string.IsNullOrWhiteSpace(role.Qualification)) roleFacts.Add($"Qualification: {role.Qualification}");
        if (role.MinYears is { } min) roleFacts.Add($"Experience required: {min}+ years");

        var facts = new List<string>
        {
            $"Name: {profile.FullName}",
            $"Headline: {profile.Headline}",
        };
        if (!string.IsNullOrWhiteSpace(profile.Location)) facts.Add($"Location: {profile.Location}");
        if (!string.IsNullOrWhiteSpace(profile.Email)) facts.Add($"Email: {profile.Email}");
        if (!string.IsNullOrWhiteSpace(profile.Phone)) facts.Add($"Phone: {profile.Phone}");
        if (!string.IsNullOrWhiteSpace(profile.Summary)) facts.Add($"Summary (verbatim CV text): {profile.Summary}");

        if (profile.Experiences.Count > 0)
        {
            var lines = profile.Experiences.Select(e =>
                $"- {e.Title}, {e.Company} ({e.StartDate} – {e.EndDate ?? "Present"}): " +
                string.Join(" | ", (e.Highlights ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            facts.Add("Experience:\n" + string.Join("\n", lines));
        }

        if (profile.Education.Count > 0)
        {
            var lines = profile.Education.Select(e =>
                $"- {e.Degree} {e.FieldOfStudy}, {e.School}" +
                (e.EndYear is { } y ? $" ({y})" : ""));
            facts.Add("Education:\n" + string.Join("\n", lines));
        }

        if (profile.Certifications.Count > 0)
        {
            var lines = profile.Certifications.Select(c =>
                $"- {c.Name}, {c.Issuer}" + (c.Year is { } cy ? $" ({cy})" : ""));
            facts.Add("Certifications:\n" + string.Join("\n", lines));
        }

        if (profile.Skills.Count > 0)
            facts.Add("Skills: " + string.Join(", ", profile.Skills.Select(s => s.Name)));

        var corpus = profile.RawText ?? "";
        if (corpus.Length > 16_000) corpus = corpus[..16_000];

        return $"""
                POSITION
                {string.Join(" | ", roleFacts.Where(f => !string.IsNullOrWhiteSpace(f)))}
                Requirements: {requirements}
                Requirement coverage (facts the CV can support):
                {string.Join("\n", coverage)}

                CANDIDATE (facts only — every FACT the document is allowed to state)
                ---
                {string.Join("\n", facts)}
                ---

                CANDIDATE'S ORIGINAL CV (verbatim, for wording and tone; facts from it
                may be restated but anything you invent is disqualifying)
                ---
                {corpus}
                ---

                Write the tailored CV as a JSON array of sections. Headings exactly:
                {string.Join(", ", build.Document.Select(s => s.Heading))}.
                The "Target" line must remain exactly: {build.Document.FirstOrDefault(s => s.Heading == "Target")?.Body}
                """;
    }

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal)) return trimmed;

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var first = trimmed.IndexOf('\n');
            if (first >= 0) trimmed = trimmed[(first + 1)..];
            var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (last >= 0) trimmed = trimmed[..last];
        }

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private sealed class RewrittenSection
    {
        public string? Heading { get; set; }
        public string? Body { get; set; }
    }
}