using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JobSuites.Api.Models;

namespace JobSuites.Api.Cv;

/// <summary>
/// Turns raw CV text into structured profile data.
///
/// Design rule: extract, never invent. Anything this cannot read with confidence
/// is left null, because a fabricated skill or a guessed year silently corrupts
/// every match score built on top of it. A CV that yields a thin profile is more
/// useful than one that yields a confident wrong one.
/// </summary>
public static class CvParser
{
    private static readonly string[] SectionHeaders =
    [
        "personal details", "personal information", "contact details", "contact information",
        "professional summary", "career summary", "professional profile", "summary", "objective",
        "work experience", "work history", "employment history", "experience", "career history",
        "education", "academic background", "academic qualifications", "qualifications",
        "skills", "technical skills", "core competencies", "competencies", "technologies",
        "certifications", "certificates", "professional certifications", "professional qualifications",
        "licenses", "training", "awards", "achievements", "projects", "languages",
    ];

    private static readonly string[] SkillCatalog = BuildSkillCatalog();

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static ParsedCv Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidCvException(
                "That file contained no readable text. If it is a scan or an image, " +
                "upload a text-based PDF or a DOCX instead.");
        }

        var lines = SplitLines(text);
        var sections = MapSections(lines);
        var fullText = string.Join('\n', lines);

        return new ParsedCv(
            FullName: DetectName(lines),
            Email: DetectEmail(fullText),
            Phone: DetectPhone(fullText),
            Location: DetectLocation(fullText),
            Headline: DetectHeadline(sections, lines),
            YearsExperience: DetectYears(sections),
            Summary: Trim(sections.GetValueOrDefault("summary"), 900),
            Skills: DetectSkills(fullText, sections),
            Experiences: DetectExperiences(sections),
            Education: DetectEducation(sections),
            Certifications: DetectCertifications(sections, fullText),
            Languages: DetectLanguages(sections, fullText),
            Links: DetectLinks(fullText));
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

    /// <summary>Maps each known section heading to the lines beneath it. Unknown
    /// sections are ignored rather than treated as content, because a heading we
    /// do not recognise is not a section we can attribute anything to.</summary>
    private static Dictionary<string, List<string>> MapSections(List<string> lines)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (current is not null && buffer.Count > 0)
            {
                result[current] = buffer.ToList();
            }
        }

        foreach (var line in lines)
        {
            var normalized = NormalizeHeading(line);
            if (normalized is not null)
            {
                Flush();
                current = normalized;
                buffer.Clear();
                continue;
            }

            if (current is not null) buffer.Add(line);
        }

        Flush();
        return result;
    }

    /// <summary>Returns a canonical section key when the line is a bare heading,
    /// else null. Requiring a bare line stops body text that happens to contain
    /// the word "experience" from being treated as a section break.</summary>
    private static string? NormalizeHeading(string line)
    {
        var cleaned = line
            .TrimEnd(':')
            .Trim()
            .ToLowerInvariant();

        if (cleaned.Length is 0 or > 40) return null;

        // Headings arrive in several styles: "Work Experience", "WORK EXPERIENCE",
        // "02. Work Experience", "Work Experience |".
        cleaned = Regex.Replace(cleaned, @"^[\d\.\s\|\-–—]+", "").Trim();
        cleaned = cleaned.TrimEnd('|', '-', '–', '—', ':', ' ').Trim();

        var match = SectionHeaders.FirstOrDefault(
            h => cleaned == h || cleaned.StartsWith(h + " ", StringComparison.Ordinal));

        return match;
    }

    private static string? DetectName(List<string> lines)
    {
        // The name is nearly always in the first few lines. Skip lines that look
        // like contact details or section headings.
        foreach (var line in lines.Take(6))
        {
            if (line.Contains('@') || Regex.IsMatch(line, @"\d{4,}")) continue;
            if (SectionHeaders.Any(h => line.TrimEnd(':').Trim().ToLowerInvariant() == h)) continue;

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 2 or > 5) continue;

            // A name is capitalised words with no sentence punctuation.
            if (line.Any(c => c is '.' or ',' or ';' or '@' or '(')) continue;
            if (!words.All(w => w.Length > 1 && char.IsUpper(w[0]))) continue;

            return line;
        }

        return null;
    }

    private static string? DetectEmail(string text)
    {
        var m = Regex.Match(text, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static string? DetectPhone(string text)
    {
        // Nigerian formats, with and without the +234 prefix, plus a general
        // international fallback.
        var patterns = new[]
        {
            @"\+?234[\s\-]?\d{3}[\s\-]?\d{3}[\s\-]?\d{4}",
            @"\b0\d{10}\b",
            @"\+\d{1,3}[\s\-]?\(?\d{3}\)?[\s\-]?\d{3}[\s\-]?\d{4}",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p);
            if (m.Success) return m.Value.Trim();
        }

        return null;
    }

    private static string? DetectLocation(string text)
    {
        // Look for a Nigerian state, or a "Lagos, Nigeria"-style line.
        var m = Regex.Match(text, @"\b(Lagos|Abuja|Port Harcourt|Ibadan|Kano|Enugu|Kaduna|Abeokuta)\b", RegexOptions.IgnoreCase);
        if (m.Success) return Culture(m.Value);

        return null;
    }

    private static string? DetectHeadline(Dictionary<string, List<string>> sections, List<string> lines)
    {
        // Preferred signal: a shouted line above the first section heading.
        // "SENIOR BACKEND ENGINEER" is a claim the candidate made about
        // themselves, so it beats anything inferred from body text.
        foreach (var line in lines)
        {
            if (SectionHeaders.Any(h =>
                    string.Equals(NormalizeHeading(line), h, StringComparison.OrdinalIgnoreCase)))
            {
                break; // reached the body of the CV
            }

            var letters = line.Where(char.IsLetter).ToArray();
            if (letters.Length < 6 || letters.Length > 60) continue;
            if (!letters.All(char.IsUpper)) continue;
            if (line.Any(c => char.IsDigit(c) || c is '@' || c is '.' or ',')) continue;

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 2 or > 8) continue;

            return line.Trim();
        }

        // Fallback: the opening line of a summary section.
        foreach (var key in new[] { "professional summary", "career summary", "summary", "professional profile", "objective" })
        {
            if (!sections.TryGetValue(key, out var summary) || summary.Count == 0) continue;
            var first = summary[0];
            return first.Length > 300 ? first[..300] : first;
        }

        return null;
    }

    /// <summary>
    /// Sums distinct employment date ranges. Overlapping ranges are not double
    /// counted, since two concurrent jobs are not two extra years of experience.
    /// </summary>
    private static int? DetectYears(Dictionary<string, List<string>> sections)
    {
        var ranges = new List<(DateTime Start, DateTime End)>();

        foreach (var key in new[] { "work experience", "work history", "employment history", "experience", "career history" })
        {
            if (!sections.TryGetValue(key, out var lines)) continue;

            foreach (var line in lines)
            {
                var (start, end) = FindDateRange(line);
                if (start is not null && end is not null) ranges.Add((start.Value, end.Value));
            }
        }

        if (ranges.Count == 0) return null;

        // Merge overlaps.
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
            {
                if (r.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, r.End);
                }
            }
            else
            {
                merged.Add(r);
            }
        }

        var months = merged.Sum(r => Math.Max(0, (r.End.Year * 12 + r.End.Month) - (r.Start.Year * 12 + r.Start.Month)));
        var years = months / 12;

        return years > 0 && years < 60 ? years : null;
    }

    private static (DateTime? Start, DateTime? End) FindDateRange(string line)
    {
        var now = DateTime.UtcNow;
        var months = MonthMap;

        // "Jan 2020 - Present", "January 2020 – Current", "2020 - 2023"
        var labelled = Regex.Match(
            line,
            @"(?<m1>[A-Za-z]{3,9})\.?\s*(?<y1>\d{4})\s*(?:-|–|—|to|until)\s*" +
            @"(?:(?<m2>[A-Za-z]{3,9})\.?\s*)?(?<y2>\d{4}|present|current|now|to date)",
            RegexOptions.IgnoreCase);

        if (labelled.Success)
        {
            var s = ToMonth(labelled.Groups["m1"].Value, months);
            var y1 = int.Parse(labelled.Groups["y1"].Value);
            if (s is not null && y1 is >= 1970 and <= 2100)
            {
                var start = new DateTime(y1, s.Value, 1);

                var tail = labelled.Groups["y2"].Value.ToLowerInvariant();
                DateTime end;
                if (tail is "present" or "current" or "now" or "to date")
                {
                    end = now;
                }
                else
                {
                    var m2 = labelled.Groups["m2"].Success
                        ? ToMonth(labelled.Groups["m2"].Value, months)
                        : s;
                    end = new DateTime(int.Parse(tail), m2 ?? s!.Value, 1);
                }

                return end > start ? (start, end) : (null, null);
            }
        }

        var iso = Regex.Match(
            line,
            @"(?<y1>(19|20)\d{2})[-/](?<m1>0?[1-9]|1[0-2])\s*(?:-|–|—|to|until)\s*(?<tail>.{0,24})",
            RegexOptions.IgnoreCase);

        if (iso.Success && !char.IsLetter(iso.Groups["tail"].Value.TrimStart()[0])
                       && char.IsDigit(iso.Groups["tail"].Value.TrimStart()[0])
            || iso.Success && Regex.IsMatch(iso.Groups["tail"].Value, @"^(present|current|now|to date)", RegexOptions.IgnoreCase))
        {
            var start = new DateTime(
                int.Parse(iso.Groups["y1"].Value), int.Parse(iso.Groups["m1"].Value), 1);

            var end = ResolveEnd(iso.Groups["tail"].Value, start);
            if (end is not null) return end > start ? (start, end) : (null, null);
        }

        var numeric = Regex.Match(line, @"(?<y1>(19|20)\d{2})\s*(?:-|–|—|to)\s*(?<y2>(19|20)\d{2})");
        if (numeric.Success)
        {
            var start = new DateTime(int.Parse(numeric.Groups["y1"].Value), 1, 1);
            var end = new DateTime(int.Parse(numeric.Groups["y2"].Value), 12, 31);
            return end > start ? (start, end) : (null, null);
        }

        return (null, null);
    }

    /// <summary>Interprets whatever followed a "YYYY-MM - " separator as an end
    /// date. Returns null when it cannot be read confidently.</summary>
    private static DateTime? ResolveEnd(string tail, DateTime start)
    {
        tail = tail.Trim();

        if (Regex.IsMatch(tail, @"^(present|current|now|to date)", RegexOptions.IgnoreCase))
        {
            return DateTime.UtcNow;
        }

        var both = Regex.Match(tail, @"(?<y>(19|20)\d{2})[-/](?<m>0?[1-9]|1[0-2])");
        if (both.Success)
        {
            return new DateTime(int.Parse(both.Groups["y"].Value), int.Parse(both.Groups["m"].Value), 1);
        }

        var named = Regex.Match(tail, @"(?<m>[A-Za-z]{3,9})\.?,?\s*(?<y>(19|20)\d{4})");
        if (named.Success && ToMonth(named.Groups["m"].Value, MonthMap) is { } nm)
        {
            return new DateTime(int.Parse(named.Groups["y"].Value), nm, 1);
        }

        var yearOnly = Regex.Match(tail, @"^(?<y>(19|20)\d{2})\b");
        if (yearOnly.Success)
        {
            // A bare year means "through the end of that year".
            return new DateTime(int.Parse(yearOnly.Groups["y"].Value), 12, 1);
        }

        return null;
    }

    private static readonly Dictionary<string, int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1, ["feb"] = 2, ["february"] = 2, ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4, ["may"] = 5, ["jun"] = 6, ["june"] = 6, ["jul"] = 7, ["july"] = 7,
        ["aug"] = 8, ["august"] = 8, ["sep"] = 9, ["sept"] = 9, ["september"] = 9, ["oct"] = 10,
        ["october"] = 10, ["nov"] = 11, ["november"] = 11, ["dec"] = 12, ["december"] = 12,
    };

    private static int? ToMonth(string token, Dictionary<string, int> map) =>
        map.TryGetValue(token.TrimEnd('.'), out var m) ? m : null;

    /// <summary>
    /// Finds skills by catalog match, then treats anything left in a skills
    /// section as an extra candidate and normalises it. Years are only attributed
    /// when the CV explicitly states them next to the skill.
    /// </summary>
    private static List<ProfileSkill> DetectSkills(string fullText, Dictionary<string, List<string>> sections)
    {
        var found = new Dictionary<string, ProfileSkill>(StringComparer.OrdinalIgnoreCase);

        var skillsText = new StringBuilder();
        foreach (var key in new[] { "skills", "technical skills", "core competencies", "competencies", "technologies" })
        {
            if (sections.TryGetValue(key, out var lines)) skillsText.AppendLine(string.Join('\n', lines));
        }

        foreach (var skill in SkillCatalog)
        {
            // Word-boundary match so "go" does not match "google" and "r" does not
            // match everywhere.
            var pattern = $@"(?<![\w+#]){Regex.Escape(skill)}(?![\w+#])";
            if (!Regex.IsMatch(fullText, pattern, RegexOptions.IgnoreCase)) continue;

            var years = ExtractExplicitYears(fullText, skill);
            found[skill] = new ProfileSkill(
                Name: skill,
                Normalized: skill.ToLowerInvariant(),
                Years: years ?? 0,
                IsCore: years is not null);
        }

        // Skills listed but not in the catalog are still real signal, especially
        // for Nigerian and finance roles the catalog may not cover.
        foreach (var line in skillsText.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var token in line.Split([',', '•', '·', '|', ';', '/', '–', '—'], StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Regex.Replace(token, @"[•·\|\-–—:]", " ").Trim();
                if (candidate.Length is < 2 or > 40) continue;
                if (Regex.IsMatch(candidate, @"^\d")) continue;
                if (found.ContainsKey(candidate)) continue;

                found[candidate] = new ProfileSkill(candidate, candidate.ToLowerInvariant(), 0, false);
            }
        }

        return found.Values
            .OrderByDescending(s => s.IsCore)
            .ThenByDescending(s => s.Years)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Looks for "React (5 years)" or "React, 5 years" near the skill
    /// mention. Returns null when no explicit duration is attached, which is the
    /// normal case and should not be guessed at.</summary>
    private static int? ExtractExplicitYears(string text, string skill)
    {
        var pattern = $@"{Regex.Escape(skill)}[^.\n]{{0,40}}?(\d{{1,2}})\s*\+?\s*(?:years?|yrs?)";
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var y) && y is >= 0 and <= 50)
        {
            return y;
        }

        var reverse = $@"(\d{{1,2}})\s*\+?\s*(?:years?|yrs?)[^.\n]{{0,30}}?{Regex.Escape(skill)}";
        var r = Regex.Match(text, reverse, RegexOptions.IgnoreCase);
        if (r.Success && int.TryParse(r.Groups[1].Value, out var y2) && y2 is >= 0 and <= 50)
        {
            return y2;
        }

        return null;
    }

    /// <summary>
    /// Extracts roles from the experience section. A line is treated as a role
    /// header when it carries a job-title-shaped token plus a date range or a
    /// company separator — both are strong signals, and requiring either one
    /// avoids turning every bullet point into a job.
    /// </summary>
    private static List<ProfileExperience> DetectExperiences(Dictionary<string, List<string>> sections)
    {
        var result = new List<ProfileExperience>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "work experience", "work history", "employment history", "experience", "career history" })
        {
            if (!sections.TryGetValue(key, out var lines)) continue;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (IsBullet(line)) continue;

                var (start, end) = FindDateRange(line);

                // A line holding nothing but a date range dates the role header
                // above it; it is not a role in its own right.
                if (IsDateOnlyLine(line)) continue;

                // The common layout is the role header, then the date range on the
                // next line. Look ahead for it.
                if (start is null && i + 1 < lines.Count && IsDateOnlyLine(lines[i + 1]))
                {
                    (start, end) = FindDateRange(lines[i + 1]);
                }

                var parts = SplitHeaderParts(StripDateRange(line));

                if (parts.Count is 0 or > 3) continue;
                if (!start.HasValue && parts.Count < 2) continue;

                // Drop the date fragment if it leaked into a part.
                parts = parts
                    .Where(p => !FindDateRange(p).Start.HasValue)
                    .Where(p => p.Length is > 1 and < 120)
                    .ToList();

                if (parts.Count < 2) continue;

                var (company, title) = ClassifyHeaderParts(parts);
                if (company is null || title is null) continue;

                var sig = $"{company}|{title}";
                if (!seen.Add(sig)) continue;

                // Highlights: the detail lines that follow, up to the next role
                // header. A trailing date-only line belongs to this role, so it is
                // skipped rather than stored as an achievement.
                var highlights = new List<string>();
                for (var j = i + 1; j < lines.Count && highlights.Count < 5; j++)
                {
                    if (IsDateOnlyLine(lines[j])) continue;
                    if (LooksLikeHeader(lines[j])) break;
                    if (lines[j].Length is < 10 or > 200) continue;
                    highlights.Add(lines[j]);
                }

                // A role is current if it has no end date yet, or ended within
                // the last two months. A job that finished last month is still
                // the most recent thing on the CV.
                var current = !end.HasValue || (DateTime.UtcNow - end.Value).TotalDays < 60;

                result.Add(new ProfileExperience(
                    Company: company,
                    Title: title,
                    StartDate: start?.ToString("MMM yyyy"),
                    EndDate: current ? "Present" : end?.ToString("MMM yyyy"),
                    IsCurrent: current,
                    Highlights: highlights.Count == 0
                        ? null
                        : string.Join(" | ", highlights)));
            }
        }

        return result;
    }

    /// <summary>True when a line carries a date range and essentially nothing
    /// else, e.g. "2021-03 - Present" sitting under a role header. A line that
    /// also holds a title or an employer is a header in its own right.</summary>
    private static bool IsDateOnlyLine(string line) =>
        line.Length <= 40
        && FindDateRange(line).Start is not null
        && !line.Contains(',')
        && !HasTitleWord(line);

    private static bool LooksLikeHeader(string line)
    {
        if (FindDateRange(line).Start is not null) return true;
        if (IsBullet(line)) return false;
        return HasTitleWord(line) && SplitHeaderParts(line).Count is >= 2;
    }

    /// <summary>Removes an inline date range such as "2021-03 - Present" from a
    /// role header before the separator split. The dash inside a date range
    /// would otherwise cut the header into fragments and leave a date as the
    /// "company".</summary>
    private static string StripDateRange(string line)
    {
        foreach (var pattern in new[]
        {
            @"(?:19|20)\d{2}-(?:0?[1-9]|1[0-2])\s*(?:-|–|—|to|until)\s*[^,|]*",
            @"[A-Za-z]{3,9}\.?\s*(?:19|20)\d{2}\s*(?:-|–|—|to|until)\s*[^,|]*",
            @"(?:19|20)\d{2}\s*(?:-|–|—|to)\s*(?:19|20)\d{2}",
        })
        {
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return (line[..m.Index] + line[(m.Index + m.Length)..])
                    .Trim()
                    .TrimEnd('|', ',', ';', ' ', '-', '–', '—');
            }
        }

        return line;
    }

    private static bool IsBullet(string line) =>
        line.Length > 0 && line[0] is '-' or '•' or '*' or '·' or '–' or '—';

    private static bool HasTitleWord(string line) =>
        TitleWords.Any(w => line.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Extracts education entries from the education sections. A line
    /// only counts when it carries a qualification token plus a school or a year,
    /// so stray mentions of "university" in a cover note are not turned into
    /// degrees.</summary>
    private static List<ProfileEducation> DetectEducation(Dictionary<string, List<string>> sections)
    {
        var result = new List<ProfileEducation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sectionKeys = new[] { "education", "academic background", "academic qualifications", "qualifications" };
        var lines = sectionKeys.SelectMany(s => sections.TryGetValue(s, out var l) ? l : []).ToList();

        foreach (var raw in lines)
        {
            if (result.Count >= 6) break;
            var line = Regex.Replace(raw, @"\s+", " ").Trim();
            if (line.Length is < 8 or > 200) continue;

            var degreeMatch = DegreePatterns.Match(line);
            if (!degreeMatch.Success) continue;

            var school = FindSchool(line);
            var (startYear, endYear) = FindYears(line);
            if (school is null && startYear is null && endYear is null) continue;

            var field = FindFieldOfStudy(line, degreeMatch.Index + degreeMatch.Length, school);

            var key = $"{degreeMatch.Value}|{school}|{startYear?.ToString() ?? ""}|{field ?? ""}";
            if (!seen.Add(key)) continue;

            result.Add(new ProfileEducation(
                School: school,
                Degree: CleanToken(degreeMatch.Value),
                FieldOfStudy: field,
                StartYear: startYear,
                EndYear: endYear,
                Details: null));
        }

        return result;
    }

    private static readonly Regex DegreePatterns = new(
        @"\b(?:B\.?Sc|M\.?Sc|B\.?Eng|M\.?Eng|B\.?Tech|M\.?Tech|B\.?Ed|B\.?A|M\.?A|M\.?B\.?A|P\.?G\.?D|Ph\.?D|PhD|HND|OND|N\.?C\.?E|SSCE|WASSCE|WAEC|NECO|GCSE|Diploma|Bachelor'?s?|Master'?s?|Doctorate|Post-graduate|Postgraduate)\b",
        RegexOptions.IgnoreCase);

    private static readonly string[] SchoolPatterns =
    [
        @"University of [A-Z][a-zA-Z'& ]{1,40}",
        @"[A-Z][a-zA-Z&']+(?: [A-Z][a-zA-Z&']+){0,2} University\b[^,|–—\d]{0,30}",
        @"(?:[A-Z][a-zA-Z&'\.]+\s+){0,2}(?:Polytechnic|College|Institute|Academy|School)(?: of [A-Z][a-zA-Z'& ]{1,30})?",
        @"University[^,|–—\d]{0,40}",
    ];

    private static string? FindSchool(string line)
    {
        foreach (var pattern in SchoolPatterns)
        {
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Value.Trim().TrimEnd(',', ' ', '.');
            }
        }

        return null;
    }

    /// <summary>The words between the degree and the school, e.g. "Computer
    /// Science" in "B.Sc Computer Science, University of Lagos". Handles the
    /// "in ..." phrasing ("Masters in Business Administration") as well.</summary>
    private static string? FindFieldOfStudy(string line, int from, string? school)
    {
        var stopIndex = school is null
            ? line.Length
            : line.IndexOf(school, StringComparison.OrdinalIgnoreCase);

        if (stopIndex <= 0) stopIndex = line.Length;

        var segment = line.Length <= from || from >= stopIndex
            ? ""
            : line[from..stopIndex];

        segment = segment.TrimStart(' ', '.', ':', '-', '–', '—');
        if (segment.StartsWith("in ", StringComparison.OrdinalIgnoreCase)) segment = segment[3..];

        // Stop at the first separator so "on list" text cannot leak in.
        var cut = segment.IndexOfAny([',', ';', '|', '(', '–', '—']);
        if (cut >= 0) segment = segment[..cut];

        segment = segment.Trim(' ', '-', ':', '.', '–', '—');

        if (segment.Length < 2 || segment.Length > 60) return null;
        if (Regex.IsMatch(segment, @"^\d")) return null;

        return segment;
    }

    /// <summary>First year is the start, second the completion. A lone year on
    /// an education line is the year the qualification was earned.</summary>
    private static (int? Start, int? End) FindYears(string line)
    {
        var years = Regex.Matches(line, @"\b((?:19|20)\d{2})\b")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(y => y is >= 1960 and <= 2100)
            .ToList();

        return years.Count switch
        {
            >= 2 => (years[0], years[1]),
            1 => (null, years[0]),
            _ => (null, null),
        };
    }

    private static readonly string[] CertBodies =
    [
        "NEBOSH", "ICAN", "ACCA", "CIMA", "CIPM", "CIPMN", "PMI", "PMP", "PRINCE2",
        "HRCI", "SHRM", "CISCO", "CCNA", "CCNP", "CompTIA", "ISACA", "CISA", "CISSP",
        "CFA", "IFRS", "CPA", "HSE", "IOSH", "First Aid", "Six Sigma", "Lean Six Sigma",
        "GNIIT", "NIIT", "British Council", "IELTS", "TOEFL", "Oracle", "AWS", "Microsoft",
        "Google", "KPMG", "Deloitte", "CIBN", "NIM", "CIPFA",
    ];

    /// <summary>Extracts certifications. Requires a certification keyword or a
    /// recognised certifying body on the line, which keeps "Best Employee 2023"
    /// awards out while still catching "NEBOSH Level 3" style entries.</summary>
    private static List<ProfileCertification> DetectCertifications(
        Dictionary<string, List<string>> sections, string fullText)
    {
        var result = new List<ProfileCertification>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sectionKeys = new[]
        {
            "certifications", "certificates", "professional certifications",
            "professional qualifications", "licenses", "training", "awards", "achievements",
        };
        var sectionText = string.Join(
            '\n', sectionKeys.SelectMany(s => sections.TryGetValue(s, out var l) ? l : []));

        foreach (var raw in sectionText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.Count >= 8) break;
            var line = Regex.Replace(raw, @"\s+", " ").Trim().TrimEnd(':');

            var isCert = Regex.IsMatch(line, @"\b(certified|certification|certificate|licensed)\b", RegexOptions.IgnoreCase);
            var body = CertBodies.FirstOrDefault(b =>
                Regex.IsMatch(line, $@"\b{Regex.Escape(b)}\b", RegexOptions.IgnoreCase));

            if (!isCert && body is null) continue;

            // A lone heading line ("CERTIFICATIONS") is not an entry.
            if (line.Length < 8 && body is null) continue;

            var name = Regex.Replace(line, @"\b(19|20)\d{2}\b", "").Trim();
            name = Regex.Replace(name, @"(issued|certified|certification|certificate|obtained|awarded) by\b[^|,–—]*", "", RegexOptions.IgnoreCase).Trim();
            name = Regex.Replace(name, @"^(?:certified|certification|certificate|certificate in|certification in)\s+", "", RegexOptions.IgnoreCase).Trim();
            name = name.TrimEnd('-', '–', '—', '|', ':', ',').Trim();

            if (name.Length < 4 || name.Length > 120) continue;

            var key = name.ToLowerInvariant();
            if (!seen.Add(key)) continue;

            var year = Regex.Match(line, @"\b((?:19|20)\d{2})\b");
            int? yearValue = year.Success && int.TryParse(year.Groups[1].Value, out var y) && y is >= 1990 and <= 2100
                ? y
                : null;

            result.Add(new ProfileCertification(
                Name: CleanToken(name),
                Issuer: body is not null && !name.Contains(body, StringComparison.OrdinalIgnoreCase) ? body : null,
                Year: yearValue));
        }

        return result;
    }

    private const string LanguageSectionKey = "languages";

    private static readonly string[] LanguageCatalog =
    [
        "english", "yoruba", "igbo", "hausa", "french", "spanish", "arabic", "german",
        "dutch", "portuguese", "italian", "chinese", "mandarin", "pidgin", "japanese",
        "korean", "russian", "swahili", "twi", "edo", "ibibio", "fulani", "kanuri",
        "tiv", "efik", "urhobo", "igala", "nupe", "gbagyi", "idoma",
    ];

    private static readonly (string Pattern, string Proficiency)[] ProficiencyHints =
    [
        (@"(native|mother tongue|first language)", "native"),
        (@"(fluent|advanced|professional|proficient|excellent|bilingual)", "professional"),
        (@"(conversational|intermediate|working)", "conversational"),
        (@"(basic|elementary|beginner)", "basic"),
    ];

    /// <summary>Reads the languages section, and falls back to scanning the CV
    /// for a language named next to a proficiency word. A language listed
    /// without a level is read as "professional", since the author chose to
    /// claim it on a CV.</summary>
    private static List<ProfileLanguage> DetectLanguages(
        Dictionary<string, List<string>> sections, string fullText)
    {
        var result = new List<ProfileLanguage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sectionLines = sections.TryGetValue(LanguageSectionKey, out var l) ? l : [];

        void Add(string line, string? globalHint)
        {
            foreach (var chunk in line.Split([',', '•', '·', ';', '/', '|'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = chunk.Trim().TrimEnd('.');
                if (trimmed.Length is < 2 or > 80) continue;

                var language = LanguageCatalog.FirstOrDefault(name =>
                    Regex.IsMatch(trimmed, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase));

                if (language is null) continue;
                if (!seen.Add(language)) continue;

                var proficiency = ProbeProficiency(trimmed) ?? ProbeProficiency(line) ?? globalHint ?? "professional";

                result.Add(new ProfileLanguage(CleanToken(language), proficiency));
                if (result.Count >= 8) return;
            }
        }

        var hint = ProbeProficiency(string.Join(' ', sectionLines));
        foreach (var line in sectionLines)
        {
            Add(line, hint);
            if (result.Count >= 8) return result;
        }

        if (result.Count == 0)
        {
            foreach (var line in fullText.Split('\n'))
            {
                if (!ProficiencyHints.Any(h => Regex.IsMatch(line, h.Pattern, RegexOptions.IgnoreCase))) continue;
                Add(line, null);
                if (result.Count >= 8) break;
            }
        }

        return result;
    }

    private static string? ProbeProficiency(string text)
    {
        foreach (var (pattern, proficiency) in ProficiencyHints)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return proficiency;
        }

        return null;
    }

    /// <summary>URLs the author actually wrote down, mapped to the label the
    /// profile uses (LinkedIn, GitHub, X, Portfolio, Website).</summary>
    private static List<ProfileLink> DetectLinks(string fullText)
    {
        var result = new List<ProfileLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in UrlPattern.Matches(fullText))
        {
            if (result.Count >= 8) break;

            var url = m.Value.Trim().TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');
            if (url.Length is < 8 or > 300) continue;
            if (!seen.Add(url)) continue;

            var label = LabelForHost(ExtractHost(url));

            // A generic http link with no recognizable profile host is not a
            // professional reference. Accept a "www." bare link only.
            if (label == "Website" && !url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new ProfileLink(
                Label: label,
                Url: url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url));
        }

        return result;
    }

    private static readonly Regex UrlPattern = new(
        @"https?://[^\s<>""']+|www\.[^\s<>""']+|(?<![A-Za-z0-9])(?:linkedin|github|gitlab|twitter|x|behance|dribbble|medium|bitbucket)\.com/[A-Za-z0-9._%+\-?=&/]+",
        RegexOptions.IgnoreCase);

    private static string? ExtractHost(string url)
    {
        var m = Regex.Match(url, @"([a-z0-9\-]+)\.(com|ng|net|org|io|me|dev)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string LabelForHost(string? host) => host switch
    {
        "linkedin" => "LinkedIn",
        "github" or "gitlab" or "bitbucket" => "GitHub",
        "twitter" => "X",
        "x" => "X",
        "behance" or "dribbble" => "Portfolio",
        "medium" => "Website",
        _ => "Website",
    };

    /// <summary>Folds stray punctuation and whitespace out of a captured token.</summary>
    private static string CleanToken(string value)
    {
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim();
        cleaned = cleaned.TrimEnd('.', ':', ',', '-', '–', '—', '|');
        return cleaned;
    }

    /// <summary>
    /// Words that follow a comma as part of the job title rather than starting
    /// the employer name, e.g. "Data Engineer, Data".
    /// </summary>
    /// <summary>
    /// Words that identify a job title. A role header must contain one of these.
    /// Requiring a title word is what stops a bullet point such as "Rebuilt the
    /// ledger service in Python, cutting settlement time" from being read as a job.
    /// </summary>
    private static readonly string[] TitleWords =
    [
        "engineer", "developer", "manager", "analyst", "consultant", "officer", "executive",
        "designer", "architect", "administrator", "assistant", "supervisor", "lead", "head",
        "director", "intern", "specialist", "technician", "accountant", "auditor", "banker",
        "scientist", "marketer", "coordinator", "associate", "lawyer", "doctor", "nurse",
        "teacher", "chef", "driver", "welder", "plumber", "mechanic", "artisan", "partner",
        "founder", "cashier", "merchandiser", "inspector", "surveyor", "estimator",
        "actuary", "underwriter", "attendant", "agent", "representative", "advisor",
    ];

    private static readonly HashSet<string> TitleSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "backend", "frontend", "front end", "back end", "full stack", "fullstack",
        "mobile", "cloud", "devops", "security", "software", "qa", "quality assurance",
        "ui", "ux", "ui/ux", "business", "technical", "digital", "product", "sales",
        "marketing", "finance", "financial", "hr", "human resources", "admin",
        "administrative", "network", "systems", "platform", "site", "web", "android",
        "ios", "scrum", "agile", "process", "procurement", "operations",
    };

    /// <summary>
    /// Splits a role header into its employer/title fragments. Handles the pipe
    /// and dash separators as well as the very common "Title, Company" comma form,
    /// while leaving "Data Engineer, Data" intact.
    /// </summary>
    private static List<string> SplitHeaderParts(string line)
    {
        var parts = Regex.Split(line, @"\s+[|–—\-]{1,2}\s+|\s*\|\s*")
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();

        if (parts.Count == 1)
        {
            var comma = parts[0].Split(", ", StringSplitOptions.None);
            if (comma.Length == 2 && !TitleSuffixes.Contains(comma[1].Trim()))
            {
                parts = comma.Select(p => p.Trim()).ToList();
            }
        }

        return parts;
    }

    /// <summary>Decides which fragment is the employer and which is the job
    /// title. Suffixed titles ("Engineer, Data") and known title words win.</summary>
    private static (string? Company, string? Title) ClassifyHeaderParts(List<string> parts)
    {
        var titleIndex = parts.FindIndex(p =>
            TitleWords.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase)));

        // No title word means this is not a role header. Extracting nothing is the
        // correct outcome; guessing here is what invented jobs out of bullets.
        if (titleIndex < 0) return (null, null);

        var title = parts[titleIndex];
        var company = titleIndex == 0
            ? (parts.Count > 1 ? parts[1] : null)
            : string.Join(" ", parts.Take(titleIndex));

        return (
            IsPlausibleName(company) ? company : null,
            IsPlausibleName(title) ? title : null);
    }

    private static bool IsPlausibleName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= 120
        && !Regex.IsMatch(value, @"^[\d\s\-\.]+$");

    private static string? Trim(List<string>? lines, int max) =>
        lines is null || lines.Count == 0
            ? null
            : string.Join(" ", lines).Length > max
                ? string.Join(" ", lines)[..max]
                : string.Join(" ", lines);

    private static string Culture(string s) =>
        char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Skills common enough in Nigerian professional CVs to be worth
    /// matching on. Extend rather than replace, and keep it alphabetised.</summary>
    private static string[] BuildSkillCatalog() =>
    [
        "Accounting", "Active Directory", "Adobe Creative Suite", "Adobe Photoshop",
        "Agile", "Ajax", "Amazon Web Services", "Android", "Angular", "Ansible",
        "Apache", "API Development", "Artificial Intelligence", "AWS", "Azure",
        "Bash", "Business Intelligence", "Business Analysis", "C#", "C++", "CAML",
        "CHANGE MANAGEMENT", "Citrix", "Cloud Computing", "COBOL", "Communication",
        "CompTIA", "Configuration Management", "Crystal Reports", "CSS", "Customer Service",
        "Cyber Security", "Data Analysis", "Data Entry", "Data Mining", "Data Modeling",
        "Database Administration", "Data Warehousing", "DevOps", "Digital Marketing",
        "Docker", "Elasticsearch", "Email Marketing", "ETL", "Excel", "Firewall",
        "Flutter", "Git", "Google Analytics", "Google Cloud", "Graphic Design", "HTML",
        "Human Resources", "IBM Cognos", "Incident Management", "Information Security",
        "Inventory Management", "iOS", "ITIL", "Java", "JavaScript", "Jenkins", "Kafka",
        "Kubernetes", "Laravel", "Leadership", "Linux", "Machine Learning", "MacOS",
        "Maintenance", "Management", "MariaDB", "Matlab", "Microsoft Access", "Microsoft Excel",
        "Microsoft Office", "Microsoft Word", "Microsoft PowerPoint", "MongoDB", "MySQL",
        "Network Administration", "Networking", "Node.js", "NOSQL", "Office 365", "Oracle",
        "Operating Systems", "Oracle Database", "PHP", "PostgreSQL", "Power BI", "Project Management",
        "Procurement", "Python", "Qlik", "R Programming", "React", "React Native", "Redis",
        "Risk Management", "SAP", "Salesforce", "SAS", "SPSS", "SQL", "SQL Server",
        "Statistics", "Tableau", "TCP/IP", "Technical Support", "TensorFlow", "Troubleshooting",
        "TypeScript", "Ubuntu", "Unix", "User Experience", "User Support", "VBA", "VMware",
        "Vue.js", "Web Design", "Web Development", "Windows Server", "WordPress", "Xero",
        "YAML",
    ];
}

public record ParsedCv(
    string? FullName,
    string? Email,
    string? Phone,
    string? Location,
    string? Headline,
    int? YearsExperience,
    string? Summary,
    List<ProfileSkill> Skills,
    List<ProfileExperience> Experiences,
    List<ProfileEducation> Education,
    List<ProfileCertification> Certifications,
    List<ProfileLanguage> Languages,
    List<ProfileLink> Links)
{
    /// <summary>
    /// Union of two independent parses of the same CV text. The deterministic
    /// regex parser is the baseline that can never lose a fact; the LLM pass may
    /// only ADD what it found truthfully. This is what keeps ingestion monotonic:
    /// a model that truncates (returns a few fields, drops skills) cannot make
    /// the profile thinner than the regex parse would — each list is deduplicated
    /// by key and the richer value wins.
    /// </summary>
    public static ParsedCv Merge(ParsedCv baseline, ParsedCv? extra)
    {
        if (extra is null) return baseline;

        var skillKeys = new HashSet<string>(
            baseline.Skills.Select(s => ProfileTaxonomy.Normalize(s.Name)),
            StringComparer.Ordinal);
        var skills = new List<ProfileSkill>(baseline.Skills);
        foreach (var skill in extra.Skills)
        {
            if (skillKeys.Add(ProfileTaxonomy.Normalize(skill.Name))) skills.Add(skill);
        }

        var expByKey = new Dictionary<string, ProfileExperience>(StringComparer.Ordinal);
        foreach (var e in baseline.Experiences) expByKey.TryAdd(MergeKey(e), e);
        foreach (var e in extra.Experiences)
        {
            var key = MergeKey(e);
            expByKey.TryGetValue(key, out var existing);
            expByKey[key] = existing is null ? e : MergeBest(e, existing);
        }
        var experiences = expByKey.Values.ToList();

        var eduKeys = new HashSet<string>(
            baseline.Education.Select(MergeKey),
            StringComparer.Ordinal);
        var education = new List<ProfileEducation>(baseline.Education);
        foreach (var e in extra.Education)
        {
            if (eduKeys.Add(MergeKey(e))) education.Add(e);
        }

        var certKeys = new HashSet<string>(
            baseline.Certifications.Select(c => ProfileTaxonomy.Normalize(c.Name)),
            StringComparer.Ordinal);
        var certs = new List<ProfileCertification>(baseline.Certifications);
        foreach (var c in extra.Certifications)
        {
            if (certKeys.Add(ProfileTaxonomy.Normalize(c.Name))) certs.Add(c);
        }

        var langKeys = new HashSet<string>(
            baseline.Languages.Select(l => ProfileTaxonomy.Normalize(l.Name)),
            StringComparer.Ordinal);
        var languages = new List<ProfileLanguage>(baseline.Languages);
        foreach (var l in extra.Languages)
        {
            if (langKeys.Add(ProfileTaxonomy.Normalize(l.Name))) languages.Add(l);
        }

        var linkKeys = new HashSet<string>(
            baseline.Links.Select(l => ProfileTaxonomy.Normalize(l.Url)),
            StringComparer.Ordinal);
        var links = new List<ProfileLink>(baseline.Links);
        foreach (var l in extra.Links)
        {
            if (linkKeys.Add(ProfileTaxonomy.Normalize(l.Url))) links.Add(l);
        }

        return new ParsedCv(
            FullName: extra.FullName ?? baseline.FullName,
            Email: extra.Email ?? baseline.Email,
            Phone: extra.Phone ?? baseline.Phone,
            Location: extra.Location ?? baseline.Location,
            Headline: extra.Headline ?? baseline.Headline,
            YearsExperience: extra.YearsExperience ?? baseline.YearsExperience,
            Summary: extra.Summary ?? baseline.Summary,
            Skills: skills,
            Experiences: experiences,
            Education: education,
            Certifications: certs,
            Languages: languages,
            Links: links);
    }

    private static ProfileExperience MergeBest(ProfileExperience? extra, ProfileExperience baseline)
    {
        if (extra is null) return baseline;
        var usesExtra = !string.IsNullOrWhiteSpace(extra.Highlights) &&
                        (string.IsNullOrWhiteSpace(baseline.Highlights) ||
                         (extra.Highlights ?? "").Length > (baseline.Highlights ?? "").Length);
        return usesExtra ? extra : baseline;
    }

    private static string MergeKey(ProfileExperience e) =>
        ProfileTaxonomy.Normalize(e.Company ?? "") + "|" + ProfileTaxonomy.Normalize(e.Title ?? "") +
        (string.IsNullOrWhiteSpace(e.StartDate) ? "" : "|" + ProfileTaxonomy.Normalize(e.StartDate));

    private static string MergeKey(ProfileEducation e) =>
        ProfileTaxonomy.Normalize(e.School ?? "") + "|" + ProfileTaxonomy.Normalize(e.Degree ?? "") +
        (e.StartYear is { } sy ? "|" + sy : "");
}
