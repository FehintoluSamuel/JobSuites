using System.Text.Json;
using System.Text.RegularExpressions;
using JobSuites.Api.Cv;
using JobSuites.Api.Models;

namespace JobSuites.Api.Services.Llm;

/// <summary>
/// LLM-assisted CV ingestion. Replaces the regex parser when a model is
/// configured: it reads the same raw text and returns the same
/// <see cref="ParsedCv"/> shape, so everything downstream — merge, hash,
/// matches — is untouched.
///
/// The "extract, never invent" rule from <see cref="CvParser"/> is carried over
/// as a prompt constraint AND structural enforcement: the model is told to leave
/// fields null/empty rather than guess, and the caller still applies the same
/// acceptance checks (an unusable parse never beats the regex fallback).
/// </summary>
public sealed class LlmCvParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILlmClient _llm;
    private readonly ILogger<LlmCvParser> _log;

    public LlmCvParser(ILlmClient llm, ILogger<LlmCvParser> log)
    {
        _llm = llm;
        _log = log;
    }

    private const string SystemPrompt =
        """
        You extract structured facts from a raw CV. You NEVER invent, guess or infer:
        if a field is not clearly present in the text, leave it null (or an empty
        list). A thin but true profile is better than a confident wrong one.

        Return ONLY a JSON object, no prose, no markdown fences, matching exactly:

        {
          "fullName": string|null, "email": string|null, "phone": string|null,
          "location": string|null, "headline": string|null,
          "yearsExperience": number|null, "summary": string|null,
          "skills": [ { "name": string, "years": number } ],
          "experiences": [ {
            "company": string, "title": string, "startDate": string|null,
            "endDate": string|null, "current": boolean, "highlights": [string]
          } ],
          "education": [ {
            "school": string|null, "degree": string|null,
            "fieldOfStudy": string|null, "startYear": number|null,
            "endYear": number|null, "details": string|null
          } ],
          "certifications": [ { "name": string, "issuer": string|null, "year": number|null } ],
          "languages": [ { "name": string, "proficiency": string|null } ],
          "links": [ { "label": string|null, "url": string } ]
        }

        Rules:
        - experiences: dates as written, e.g. "Mar 2021" or "2021-03"; "current": true
          when the role has no end date. highlights: the role's bullet achievements,
          verbatim, without the leading bullet symbols.
        - skills.years: only when the CV attaches a duration to that exact skill.
        - summary: the profile/objective paragraph, verbatim.
        - Never reformat names, companies or credentials; copy them as written.
        """;

    public async Task<ParsedCv?> TryParseAsync(string rawText, CancellationToken ct)
    {
        if (!_llm.Enabled || string.IsNullOrWhiteSpace(rawText)) return null;

        var text = rawText.Length > 30_000 ? rawText[..30_000] : rawText;
        var content = await _llm.CompleteAsync(SystemPrompt, "Extract this CV:\n\n" + text, ct);
        if (content is null) return null;

        var json = StripFences(content);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var parsed = MapDoc(doc.RootElement);
            if (parsed is null) _log.LogWarning("LLM CV parse produced no usable structure.");
            return parsed;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "LLM CV parse returned invalid JSON.");
            return null;
        }
    }

    private ParsedCv? MapDoc(JsonElement root)
    {
        var skills = ReadArray(root, "skills")
            .Select(e =>
            {
                var name = Str(e, "name");
                var years = Int(e, "years") is int y && y is >= 0 and <= 50 ? y : 0;
                return name is null ? null :
                    new ProfileSkill(name.Trim(), name.Trim().ToLowerInvariant(), years, years > 0);
            })
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        var experiences = ReadArray(root, "experiences")
            .Select(MapExperience)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        var education = ReadArray(root, "education")
            .Select(e => new ProfileEducation(
                School: Cut(Str(e, "school"), 120),
                Degree: Cut(Str(e, "degree"), 120),
                FieldOfStudy: Cut(Str(e, "fieldOfStudy"), 120),
                StartYear: Int(e, "startYear") is var sy and >= 1900 and <= 2100 ? sy : null,
                EndYear: Int(e, "endYear") is var ey and >= 1900 and <= 2100 ? ey : null,
                Details: Cut(Str(e, "details"), 300),
                Source: "cv"))
            .Where(e => e.School is not null || e.Degree is not null)
            .ToList();

        var certifications = ReadArray(root, "certifications")
            .Select(e => new ProfileCertification(
                Name: Cut(Str(e, "name"), 120) ?? string.Empty,
                Issuer: Cut(Str(e, "issuer"), 120),
                Year: Int(e, "year") is var y and >= 1990 and <= 2100 ? y : null,
                Source: "cv"))
            .Where(c => c.Name.Length > 0)
            .ToList();

        var languages = ReadArray(root, "languages")
            .Select(e => new ProfileLanguage(
                Name: Cut(Str(e, "name"), 60) ?? string.Empty,
                Proficiency: Cut(Str(e, "proficiency"), 30) ?? "professional",
                Source: "cv"))
            .Where(l => l.Name.Length > 0)
            .ToList();

        var links = ReadArray(root, "links")
            .Select(e => new ProfileLink(
                Label: Cut(Str(e, "label"), 30) ?? "Website",
                Url: Cut(Str(e, "url"), 300) ?? string.Empty,
                Source: "cv"))
            .Where(l => l.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || l.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A document with zero signal is indistinguishable from a hallucinated
        // empty parse — tell the caller so it can keep the regex result.
        if (skills.Count == 0 && experiences.Count == 0 && education.Count == 0
            && certifications.Count == 0 && languages.Count == 0 && links.Count == 0)
        {
            return null;
        }

        return new ParsedCv(
            FullName: Cut(Str(root, "fullName"), 120),
            Email: Cut(Str(root, "email"), 200),
            Phone: Cut(Str(root, "phone"), 40),
            Location: Cut(Str(root, "location"), 120),
            Headline: Cut(Str(root, "headline"), 200),
            YearsExperience: Int(root, "yearsExperience") is var y and >= 0 and <= 60 ? y : null,
            Summary: Cut(Str(root, "summary"), 900),
            Skills: skills,
            Experiences: experiences,
            Education: education,
            Certifications: certifications,
            Languages: languages,
            Links: links);
    }

    private ProfileExperience? MapExperience(JsonElement e)
    {
        var company = Cut(Str(e, "company"), 120);
        var title = Cut(Str(e, "title"), 120);
        if (company is null || title is null) return null;

        var current = e.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.True;
        var endRaw = Str(e, "endDate");
        var end = NormalizeDate(endRaw, out var currentFromDate);
        // "current" flag wins; a blank end date also reads as present.
        if (current || currentFromDate || string.IsNullOrWhiteSpace(endRaw)) current = true;

        var highlights = ReadArray(e, "highlights")
            .Select(h => h.ValueKind == JsonValueKind.String && h.GetString() is { } s
                ? Regex.Replace(s, @"^[\s•·\-–—]+", "").Trim()
                : string.Empty)
            .Where(h => h.Length is > 0 and <= 200)
            .Take(5)
            .ToList();

        var endText = current ? "Present" : end;
        return new ProfileExperience(
            Company: company.Trim(),
            Title: title.Trim(),
            StartDate: NormalizeDate(Str(e, "startDate"), out _),
            EndDate: endText,
            IsCurrent: current,
            Highlights: highlights.Count == 0 ? null : string.Join(" | ", highlights),
            Source: "cv");
    }

    /// <summary>Keeps a date that already reads like the CvParser output
    /// ("Mar 2021", "Present"), else normalises a standard form to "MMM yyyy".
    /// Garbage stays null — never invented.</summary>
    private static string? NormalizeDate(string? raw, out bool missing)
    {
        missing = string.IsNullOrWhiteSpace(raw);
        if (missing) return null;

        var value = raw!.Trim();
        if (value.Equals("present", StringComparison.OrdinalIgnoreCase))
        {
            return "Present";
        }

        if (Regex.IsMatch(value, @"^(?:\d{1,2}\s+)?[A-Za-z]{3}\.\s+\d{4}$"))
        {
            return value;
        }
        if (Regex.IsMatch(value, @"^(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}$",
            RegexOptions.IgnoreCase))
        {
            return char.ToUpperInvariant(value[0]) + value[1..];
        }

        if (DateTime.TryParse(value, out var parsed))
        {
            return parsed.ToString("MMM yyyy");
        }

        return null;
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (int?)v.GetInt32()
            : null;

    private static List<JsonElement> ReadArray(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : [];

    /// <summary>Models sometimes wrap JSON in ```json fences. Strip the outer
    /// shell and keep the first brace-delimited JSON document.</summary>
    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var first = trimmed.IndexOf('\n');
            if (first >= 0) trimmed = trimmed[(first + 1)..];
            var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (last >= 0) trimmed = trimmed[..last];
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}