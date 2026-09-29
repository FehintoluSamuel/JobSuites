using System.Text.RegularExpressions;
using JobSuites.Api.Models;

namespace JobSuites.Api.Matching;

/// <summary>
/// One thing a role asks for, normalised out of the posting.
///
/// docs/PRODUCT.md §3 defines <c>jd_requirement</c> as the join target for the
/// whole product: matching scores against it, tailoring selects facts to cover
/// it, and prep generates questions for the ones left uncovered. Extracting it
/// once here is what keeps those three answers consistent — if tailoring and
/// prep each parsed the description themselves they would disagree about what
/// "uncovered" means, and the gap surface would lie.
/// </summary>
/// <param name="Category">skill | experience | education | certification |
/// language | domain, or null when the posting made a demand we could not
/// classify. Null is kept rather than defaulted: guessing a kind would put a
/// requirement in front of the user that the employer never asked for.</param>
/// <param name="Span">The verbatim JD text this was read from. The evidence a
/// verdict cites; never paraphrased, because a paraphrase cannot be checked
/// against the posting.</param>
public record RoleRequirement(
    string Key,
    string Text,
    string? Category,
    bool MustHave,
    int? YearsMin = null,
    string? Span = null);

/// <summary>
/// Pulls requirements out of a role, reading the JD body rather than only the
/// adapter's metadata fields.
///
/// The earlier version looked at <c>role.Skills</c>, qualification and years —
/// all of which are taxonomy the board already structured for us. That misses
/// the ask the description actually makes in prose ("degree in engineering",
/// "NEBOSH certified", "5+ years in a manufacturing environment"), which is
/// most of what a posting is for. Nothing here calls a model, so the same role
/// always yields the same requirement set, and a stored
/// <c>tailored_document</c> still means what it meant when it was written.
///
/// Two rules do the real work:
///
///   1. <b>Read must-have off the JD's own wording.</b> A requirement is
///      disqualifying if the description says so ("must have", "required",
///      "essential") or files it under a requirements heading and does not
///      downgrade it. The old heuristic — "the first three skills are
///      must-have" — was a guess dressed as a rule, and it silently decided
///      which gaps got shown to the user.
///   2. <b>Never emit a requirement with no evidence.</b> Every row carries the
///      line it came from. A requirement we cannot point at in the posting is
///      not shown, because "uncovered" is a claim about the candidate and it
///      has to be checkable.
///
/// A description that yields nothing is not an error: the caller falls back to
/// the taxonomy-derived set, so a role with a malformed JD still matches on what
/// we do know rather than vanishing from the queue.
/// </summary>
public static class RoleRequirements
{
    /// <summary>Phrases that make a requirement disqualifying rather than wanted.
    /// Matched as whole words so "requirement" in "no specific requirement" does
    /// not read as a must-have.</summary>
    private static readonly Regex MustCue = new(
        @"(\bmust(?:\s+|-)+(?:have|has|having|be|includes?)\b"
        + @"|\bmust-haves?\b|\brequired\b|\brequires?\b|\bessential\b|\bmandatory\b"
        + @"|\byou\s+will\s+need\b|\bwe\s+need\b|\bcompetent\s+in\b|\bproficient\s+in\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Phrases that downgrade a requirement to "wanted, not required".
    ///
    /// Checked before <see cref="MustCue"/>, and deliberately generous: the weak
    /// qualifiers ("familiarity with", "working knowledge of", "exposure to")
    /// read as lesser asks even when the bullet sits inside a requirements
    /// block. Understating a must-have costs us some emphasis in a tailored CV.
    /// Overstating one tells a candidate they lack something they have, which is
    /// the failure that costs the product its credibility — so the error is
    /// resolved towards "wanted".</summary>
    private static readonly Regex PreferredCue = new(
        @"\b(preferred|preference|desirable|advantageous|nice\s+to\s+have|"
        + @"(added|added-value)\s+advantage|bonus|an?\s+asset|would\s+be\s+a\s+plus|"
        + @"familiarity|working\s+knowledge|basic|intermediate|some\s+(knowledge|experience)|"
        + @"exposure|competent\s+in|working\s+proficiency)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Headings whose contents are the requirements block. A JD
    /// conventionally lists what you must have here and what is merely welcome
    /// under a separate heading, so the heading carries the obligation.</summary>
    private static readonly Regex RequirementsHeading = new(
        @"\b(qualifications?|requirements?|must[\s-]*haves?|key\s+(skills|competencies)|"
        + @"person(nel)?\s+specification|what\s+you\s+(need|are\s+looking\s+for)|"
        // "Required Experience & Qualifications" is one of the commonest
        // headings on this board. Without "required" here it fell through as a
        // body line and was stored as a must-have requirement of its own. The
        // noun is required in this pair: an optional one made every line
        // opening with "Key" a heading, including "Key account management".
        + @"(essential|minimum|mandatory|required|key|preferred)\s+"
        + @"(criteria|qualifications?|requirements?|experience|skills?|competenc\w+|abilities)"
        + @"|(skills?|competenc\w+)\s+(and|&)\s+(experience|competence)"
        + @"|(skills|competencies))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Headings that mark the block as optional. Listed so a preferred
    /// section cannot be mistaken for a requirements one.</summary>
    private static readonly Regex PreferredHeading = new(
        @"\b((added|additional|desirable|preferred|optional|nice)\s+"
        + @"(advantage|qualifications?|skills?|requirements?)?|"
        + @"(it\s+would\s+be|an)\s+(advantage|asset|plus)|bonus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A heading is the label itself, so a requirement that merely
    /// mentions the word is never mistaken for one. "NEBOSH certified is an
    /// added advantage" is a requirement that says the word "advantage";
    /// "Added Advantage" is the section that follows it. Only the second is a
    /// heading, and reading it as the first would delete a real requirement.</summary>
    private static bool IsHeadingLine(string text, Regex pattern)
    {
        var label = ListMarker.Replace(text, "").Trim();
        if (label.Length == 0 || label.Length > 60) return false;
        if (label.Contains('.') || label.Contains(';') || label.Contains(',')) return false;

        var match = pattern.Match(label);
        if (!match.Success) return false;

        // The label has to open with the keyword, and be short enough that it
        // cannot be a sentence describing a demand.
        var before = label[..match.Index].Trim();
        if (before.Length > 0) return false;

        return label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
    }

    /// <summary>Bullet and numbering noise, stripped before a line is read as a
    /// label: "- Added Advantage" and "3. Qualifications" are both headings.</summary>
    private static readonly Regex ListMarker = new(
        @"^[\s\-*\u2022\u00b7\u2013\u2014]+|\d+[.)]\s+", RegexOptions.Compiled);

    /// <summary>Headings that mark the duties block. Not a requirements block, so
    /// it must not lend its bullets a must-have — and the text under it is mostly
    /// prose about the job rather than asks.</summary>
    private static readonly Regex ResponsibilitiesHeading = new(
        @"\b(responsibilit\w*|duties|job\s+description|overview|"
        + @"key\s+(responsibilit\w*|duties|accountabilities)|"
        + @"about\s+(the\s+)?(role|company|us)|what\s+you\s+will\s+do|"
        + @"the\s+role|role\s+summary|job\s+summary|why\s+you|we\s+are|our\s+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EducationCue = new(
        @"\b(degree|diploma|b\.?s\.?c|h\.?n\.?d|b\.?a|b\.?tech|master'?s?|m\.?s\.?c|"
        + @"mba|ph\.?d|university|polytechnic|college|school\s+of|faculty\s+of|"
        + @"(graduat|postgraduat)(e|ion)?|o'?level|a'?level)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CertificationCue = new(
        // "certif" is the stem both forms share: certific*ation and certifi*ed.
        // Matching only the first spelled every "NEBOSH certified" posting as
        // having no certification requirement at all.
        @"\b(certif\w*|licen[cs]ed?\b|accredit\w*|nibss|nebsosh|cisa|cisae|cfa\b|"
        + @"cpa\b|cipfa|cipm|acib|pmp|prince2|itil|iso\s?9001|six\s+sigma|"
        + @"(professional|chartered|registered)\s+(certif\w*|member|membership))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LanguageCue = new(
        @"\b(languages?|english|french|arabic|hausa|igbo|yoruba|swahili|bilingual|"
        + @"(fluent|fluency|working|excellent)\s+(english|french|arabic))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExperienceCue = new(
        @"\b(\d+\s*\+?\s*(?:-|–|to)?\s*\d*\s*(?:years?|yrs?)\b|years?\s+(?:of\s+)?"
        + @"(?:proven\s+|relevant\s+|hands-on\s+|working\s+|practical\s+)?experience|"
        + @"(extensive|substantial|considerable)\s+experience|track\s+record|"
        + @"(experience|background)\s+(?:in|of|within))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DomainCue = new(
        @"\b(industry|sector|environment|firm|organisation|organization|market|"
        + @"(work|operat)\w*\s+(in|within|for)|background\s+in|exposure\s+to|"
        + @"(stakeholder|client|customer)s?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"5+ years", "5 - 8 years", "at least 5 years". The number before
    /// the range is the floor, which is the only part matching can act on.</summary>
    private static readonly Regex YearsPattern = new(
        @"(?<min>\d{1,2})\s*\+?\s*(?:[-–]|to)?\s*(?:\d{1,2})?\s*(?:years?|yrs?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lines that are boilerplate in every Nigerian listing and carry no
    /// requirement. Without this the extractor returns "send your CV" as a
    /// demand, which then shows up in the gap surface as something the candidate
    /// does not have.</summary>
    private static readonly Regex Boilerplate = new(
        @"\b(send\s+(your|us)\s+(cv|resume)|apply\s+(now|via|by)|click\s+(here|below)|"
        + @"(interested|interested\s+applic\w+)\s+(candidates?|applic\w+)|"
        + @"(email|forward)\s+(your|us)\s+(cv|resume|application)|"
        + @"(shortlisted|shortlist)\s+(candidates?|applic\w+)|"
        + @"(visit|visit\s+our)\s+(website|site|portal)|"
        + @"(responsib\w+|duties)\b.{0,40}\b(may|include|will\s+include)|"
        + @"\b(mtc|mmtc|methodist)\b|"
        + @"(please\s+note|kindly\s+note|note\s+that|applic\w+\s+are\s+responsible))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A line too short to be a requirement — "Skills:", "Requirements"
    /// with nothing under it, a stray bullet.</summary>
    private const int MinLineLength = 12;

    /// <summary>Display and storage bounds. A JD line can run to several hundred
    /// characters; the requirement label does not need all of it, and the span is
    /// capped well above any single line so it stays verbatim.</summary>
    private const int MaxText = 240;
    private const int MaxSpan = 600;

    /// <summary>Ceiling on requirements per role. A long JD can enumerate forty
    /// duties; a gap surface of forty items is not a gap surface. Must-haves are
    /// kept first, so truncation can only ever drop the optional ones.</summary>
    private const int MaxRequirements = 24;

    public static List<RoleRequirement> Extract(Role role)
    {
        // Stored rows win when they are loaded. They were extracted once, shared
        // by every user of the role, and carry the spans a later re-parse might
        // phrase differently.
        if (role.Requirements is { Count: > 0 })
        {
            return role.Requirements
                .OrderByDescending(r => r.MustHave)
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .Select(r => new RoleRequirement(
                    Key: r.Key,
                    Text: r.Text,
                    Category: r.Category,
                    MustHave: r.MustHave,
                    YearsMin: r.YearsMin,
                    Span: r.Span))
                .ToList();
        }

        return FromDescription(role);
    }

    /// <summary>
    /// Extraction proper, kept separate from <see cref="Extract"/> so the ingest
    /// path can force a re-read of a role's description even when stale rows are
    /// loaded on the entity.
    /// </summary>
    public static List<RoleRequirement> FromDescription(Role role)
    {
        var found = new Dictionary<string, RoleRequirement>(StringComparer.Ordinal);
        var jd = role.Description ?? "";

        void Offer(RoleRequirement requirement)
        {
            // First writer wins, and must-haves are offered first, so a must-have
            // reading of a line is never overwritten by a weaker one discovered
            // later for the same key.
            if (!found.TryGetValue(requirement.Key, out var existing))
            {
                found[requirement.Key] = requirement;
                return;
            }

            if (requirement.MustHave && !existing.MustHave)
                found[requirement.Key] = requirement;
            else if (string.IsNullOrWhiteSpace(existing.Span) && !string.IsNullOrWhiteSpace(requirement.Span))
                found[requirement.Key] = existing with { Span = requirement.Span };
        }

        // Pass 1: sentence-level requirements. Must-haves first so they survive
        // the cap below.
        foreach (var line in Lines(jd))
        {
            if (line.IsHeading) continue;

            var category = Category(line.Text);
            var years = Years(line.Text);

            // A line the posting calls disqualifying is a requirement whether or
            // not we can name its kind. This is the common case, not the edge
            // case: myjobmag frequently leaves the structured skill list empty
            // and states the real demand in prose ("must have hands-on
            // experience with DCS and SCADA"). Requiring a category cue would
            // have thrown exactly those away. The category is left null because
            // guessing one is how a role starts asking for a certification the
            // employer never mentioned.
            if (category is null && years is null && !MustCue.IsMatch(line.Text)) continue;

            Offer(new RoleRequirement(
                Key: years is { } y
                    ? $"experience:{y}"
                    : $"{category ?? "requirement"}:{Normalize(Excerpt(line.Text))}",
                Text: Excerpt(line.Text),
                Category: category,
                MustHave: IsMust(line),
                YearsMin: years,
                Span: line.Text));
        }

        // Pass 2: skills. Each one cites the line it was named on, so a skill
        // requirement is as traceable as a sentence one.
        foreach (var skill in (role.Skills ?? []).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var trimmed = skill.Trim();
            var span = FindSpan(jd, trimmed) ?? role.Title;
            if (string.IsNullOrWhiteSpace(span)) span = null;

            Offer(new RoleRequirement(
                Key: $"skill:{Normalize(trimmed)}",
                Text: trimmed,
                Category: "skill",
                MustHave: span is not null && IsMust(SpanLine(span)),
                Span: span));
        }

        // Pass 3: the structured fields. These are what the board itself
        // publishes, so they are as much part of the JD as the prose, and a role
        // whose description is thin still gets a requirement set.
        if (!string.IsNullOrWhiteSpace(role.Qualification))
        {
            Offer(new RoleRequirement(
                Key: $"education:{Normalize(role.Qualification)}",
                Text: role.Qualification.Trim(),
                Category: "education",
                MustHave: true,
                Span: FindSpan(jd, role.Qualification) ?? role.Qualification));
        }

        if (role.MinYears is { } min && min > 0)
        {
            Offer(new RoleRequirement(
                Key: $"experience:{min}",
                Text: $"{min}+ years of experience",
                Category: "experience",
                MustHave: true,
                YearsMin: min,
                Span: FindSpan(jd, $"{min}") ?? null));
        }

        var ordered = found.Values
            .OrderByDescending(r => r.MustHave)
            .ThenBy(r => r.Category, StringComparer.Ordinal)
            .ThenBy(r => r.Text, StringComparer.Ordinal)
            .Take(MaxRequirements)
            .ToList();

        // Nothing readable in the description and no structured fields either.
        // The caller falls back rather than treating this as a usable role.
        return ordered;
    }

    /// <summary>True when the text is worth reading as a requirement at all.
    /// Used by callers that want to know whether extraction found anything.</summary>
    public static bool IsUsable(List<RoleRequirement> requirements) => requirements.Count > 0;

    /// <summary>Classifies one line, or null when it states no ask.</summary>
    private static string? Category(string text)
    {
        if (EducationCue.IsMatch(text)) return "education";
        if (CertificationCue.IsMatch(text)) return "certification";
        if (LanguageCue.IsMatch(text)) return "language";
        if (ExperienceCue.IsMatch(text)) return "experience";
        if (DomainCue.IsMatch(text)) return "domain";
        return null;
    }

    /// <summary>The lower bound of an explicit year range, if the line states one.</summary>
    private static int? Years(string text)
    {
        var m = YearsPattern.Match(text);
        if (!m.Success) return null;
        return int.TryParse(m.Groups["min"].Value, out var min) ? min : null;
    }

    /// <summary>
    /// Whether the JD makes this disqualifying. The JD's own wording decides it,
    /// with the block it sits under as the fallback — a bullet under "Key Skills"
    /// is a requirement; the same bullet under "Added Advantage" is not.
    /// </summary>
    private static bool IsMust(Line line)
    {
        if (PreferredCue.IsMatch(line.Text)) return false;
        if (MustCue.IsMatch(line.Text)) return true;
        return line.InRequirements;
    }

    /// <summary>Re-reads a span on its own, for the skill pass, which has the
    /// line text but not the block it came from. Skills named in the requirements
    /// block are matched by their own cue where they have one.</summary>
    private static Line SpanLine(string text)
    {
        var isPref = IsHeadingLine(text, PreferredHeading);
        var inBlock = !isPref && IsHeadingLine(text, RequirementsHeading);
        return new Line(text, inBlock, isPref, IsHeading: false);
    }

    /// <summary>The JD line a term first appears on, used as evidence.</summary>
    private static string? FindSpan(string jd, string term)
    {
        var needle = term.Trim();
        if (needle.Length < 3) return null;

        foreach (var line in Lines(jd))
        {
            if (line.IsHeading) continue;
            if (line.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)) return line.Text;
        }

        return null;
    }

    /// <summary>The obligation frame a posting wraps its real demand in:
    /// "Applicants must have X", "The successful candidate should possess X".
    ///
    /// Left in place it crowds out the demand, because a gap list shows this
    /// text to someone deciding whether they can do the job. "Applicants must
    /// have hands-on experience …" is not something a candidate can check
    /// themselves against; "hands-on experience with DCS and SCADA" is. The
    /// wording is preserved in <c>Span</c>, so nothing is lost — this only
    /// cleans what is put in front of the user.</summary>
    private static readonly Regex ObligationFrame = new(
        @"^\s*(?:(?:the\s+)?(?:(?:successful|ideal|right|qualified|strong|enthusiastic|driven|experienced)\s+)?"
        + @"(?:applicant|candidate|person|professional|engineer|manager|technician)s?[\s,:]*"
        + @"(?:(?:must|should|shall|will|would|is|are)\s+)?"
        + @"(?:have|has|possess|hold|be\s+able\s+to|be\s+expected\s+to\s+have|"
        + @"be\s+required\s+to\s+have|need\s+to\s+have|required\s+to\s+have|want)[\s,:]*"
        + @"|(?:must|should|shall)\s+(?:have|possess|be\s+able\s+to|hold)[\s,:]*"
        + @"|(?:you|we|the\s+team|applicants?)\s+(?:will|would|must|should|need|require|"
        + @"are\s+looking\s+for|are\s+seeking)\s+(?:to\s+)?"
        + @"(?:have|possess|hold|be\s+able\s+to|show)?\s*(?:need|required)?\s*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Trims a line down to something readable in a gap list, without
    /// cutting mid-word.</summary>
    private static string Excerpt(string text)
    {
        var trimmed = ListMarker.Replace(text.Trim(), "").Trim();
        trimmed = ObligationFrame.Replace(trimmed, "").Trim();

        // A frame-only line ("Applicants must have") leaves nothing to show, so
        // the original wording stands rather than an empty gap entry.
        if (trimmed.Length < MinLineLength) return text.Trim();

        if (trimmed.Length <= MaxText) return trimmed;

        var cut = trimmed.LastIndexOf(' ', MaxText);
        return (cut > MaxText / 2 ? trimmed[..cut] : trimmed[..MaxText]).TrimEnd(',', ';', '-') + "…";
    }

    /// <summary>
    /// Walks the description, tracking which requirements block each line sits
    /// under so a bullet inherits its heading's obligation.
    /// </summary>
    private static IEnumerable<Line> Lines(string jd)
    {
        var inRequirements = false;

        foreach (var raw in (jd ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = raw.Trim();

            // Headings are short, keyword-first lines with no sentence
            // punctuation. Anything longer is body text that merely mentions
            // "qualifications" in passing, and is mined instead of skipped.
            if (text.Length <= 80 && !text.Contains('.') && !text.Contains(';'))
            {
                var isPrefHeading = IsHeadingLine(text, PreferredHeading);
                var isReqHeading = !isPrefHeading && IsHeadingLine(text, RequirementsHeading);
                var isResp = IsHeadingLine(text, ResponsibilitiesHeading);

                if (isReqHeading || isPrefHeading || isResp)
                {
                    inRequirements = isReqHeading;
                    yield return new Line(text, inRequirements, isPrefHeading, IsHeading: true);
                    continue;
                }
            }

            if (text.Length < MinLineLength) continue;
            if (Boilerplate.IsMatch(text)) continue;

            yield return new Line(text, inRequirements, false, IsHeading: false);
        }
    }

    private readonly record struct Line(string Text, bool InRequirements, bool Preferred, bool IsHeading);

    /// <summary>Shared normalisation so a fact and a requirement written
    /// differently ("Power BI" / "power-bi") still join. Deliberately the same
    /// rule as the matcher: one normalisation, or the two disagree.</summary>
    public static string Normalize(string value) =>
        Regex
            .Replace(value.ToLowerInvariant(), @"[^\w\+#\s]", " ")
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
}
