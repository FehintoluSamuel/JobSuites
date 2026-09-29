using System.Text.RegularExpressions;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;

namespace JobSuites.Api.Tailoring;

/// <summary>
/// Re-reads a finished tailored document and proves every claim in it came from
/// the candidate's own CV.
///
/// This is the feature, not a safety net around one. "ChatGPT writes your CV" is
/// a commodity; being able to show a user that nothing was invented is the
/// differentiator (docs/PRODUCT.md §6.2), and it is cheap precisely because it
/// is a set of string lookups over a fact table rather than a model judgement.
///
/// Three passes, all deterministic:
///
///   1. Numbers. Any figure in the document must occur in the CV. This catches
///      the invented "led a team of 12" when the CV says 8.
///   2. Proper nouns. Every capitalised token must occur in the CV, minus a
///      fixed stoplist of words this codebase's own templates introduce and
///      minus the role title and company, which are facts about the job rather
///      than claims about the candidate.
///   3. Role-skill backing. A skill the posting asks for may only appear in the
///      document if the candidate actually has it. The current generator already
///      enforces this while assembling, so the pass should never fire — it is
///      here so that a future change to the generator cannot quietly break the
///      guarantee without this check noticing.
///
/// A false positive costs the user a review prompt, so the stoplist is small and
/// auditable rather than broad. Nothing is allowed through by a catch-all.
/// </summary>
public static class FabricationCheck
{
    /// <summary>Words the tailoring templates and date formatting introduce that
    /// are not claims about the candidate. Months are here because an end date
    /// reads "Mar 2021" on a document whose corpus stores "2021-03".</summary>
    private static readonly HashSet<string> TemplateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Present", "Core", "Supporting", "Focus",
        "January", "February", "March", "April", "May", "June", "July",
        "August", "September", "October", "November", "December",
        "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Sept", "Oct", "Nov", "Dec",
        "Target", "Contact", "Summary", "Experience", "Skills", "Education",
        "Certifications", "Languages", "Role",
    };

    /// <summary>Common English verbs the tailoring pass may legitimately introduce
    /// when rewording prose. A fabrication under the §6.2 contract is an invented
    /// ENTITY — a company, name, date, number or technology noun. A verb rephrase
    /// ("led" → "managed") is wording, not a new claim, so a small, auditable list
    /// of everyday resume verbs is exempt rather than a broad dictionary that would
    /// let anything past. Anything not on this list is still blocked.</summary>
    private static readonly HashSet<string> ProseVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Achieved", "Acted", "Adjusted", "Analyzed", "Assembled", "Assisted", "Automated",
        "Built", "Collaborated", "Conducted", "Coordinated", "Created", "Cut", "Delivered",
        "Deputized", "Designed", "Developed", "Directed", "Drove", "Established", "Evaluated",
        "Exceeded", "Executed", "Expanded", "Facilitated", "Followed", "Handled", "Improved",
        "Increased", "Introduced", "Launched", "Led", "Maintained", "Managed", "Monitored",
        "Operated", "Optimized", "Oversaw", "Performed", "Presented", "Produced", "Provided",
        "Redesigned", "Reduced", "Refined", "Represented", "Resolved", "Reviewed", "Set",
        "Spearheaded", "Streamlined", "Supported", "Strengthened", "Trained", "Transformed",
        "Worked",
    };

    /// <summary>Ordinary professional vocabulary that only shows up capitalised
    /// because it opens a sentence. Applied on the LLM pass alone (see
    /// <c>freeRewrite</c>): a model genuinely rephrasing prose will open a bullet
    /// with "Ensured…" or "Sprints…", and rejecting those would reject every
    /// rewrite for a comma.
    ///
    /// The gate is the uppercase first letter, not the word's nature — these are
    /// still checked against the CV, they are simply not accusations. Nothing here
    /// is a plausible employer, school, credential or person's name, and job titles
    /// are deliberately absent: "Accountant" must not pass just because the CV
    /// happens to say "Consultant". Acronyms are absent for the same reason —
    /// "SQL" and "ERP" are claims, "KPIs" is not worth the argument.</summary>
    private static readonly HashSet<string> ProseNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Addressed", "Advised", "Aligned", "Anticipated", "Assessed", "Assigned",
        "Audited", "Benchmarked", "Charted", "Circulated", "Clarified", "Commenced",
        "Communicated", "Compiled", "Completed", "Composed", "Confirmed", "Consolidated",
        "Consulted", "Contributed", "Converted", "Crafted", "Decided", "Delegated",
        "Diagnosed", "Documented", "Drafted", "Earned", "Enabled", "Endorsed",
        "Engineered", "Ensured", "Escalated", "Estimated", "Examined", "Expedited",
        "Forecasted", "Formulated", "Gained", "Gathered", "Generated", "Governed",
        "Grew", "Guided", "Highlighted", "Identified", "Illustrated", "Implemented",
        "Informed", "Initiated", "Instrumented", "Investigated", "Justified",
        "Leveraged", "Mapped", "Measured", "Mentored", "Mitigated", "Mobilized",
        "Narrowed", "Negotiated", "Nurtured", "Orchestrated", "Outlined", "Overhauled",
        "Owned", "Partnered", "Pioneered", "Planned", "Prioritized", "Profiled",
        "Projected", "Proposed", "Pursued", "Quantified", "Rebuilt", "Reconciled",
        "Recommended", "Reorganized", "Reported", "Retained", "Returned", "Reworked",
        "Scheduled", "Secured", "Segmented", "Shipped", "Sought", "Solved", "Sourced",
        "Standardized", "Steered", "Supervised", "Surveyed", "Tackled", "Tracked",
        "Translated", "Triaged", "Tuned", "Unified", "Validated", "Verified",
        "Visualized", "Welcomed", "Widened", "Objectives", "Responsibilities",
        "Requirements", "Deliverables", "Stakeholders", "Workflows", "Sprints",
        "Milestones", "Budgets", "Forecasts",
    };

    /// <summary>Tokens carrying a digit, and proper nouns, are the two things a
    /// fabrication realistically takes the form of.</summary>
    private static readonly Regex TokenPattern = new(@"[A-Za-z0-9][A-Za-z0-9+#./&-]*", RegexOptions.Compiled);

    /// <summary>Sentence punctuation glued onto a token by the pattern above.
    /// "Management." must be checked against the corpus's "Management" — the
    /// period is the template's, not the claim's. Trailing-only, so "3.5" and
    /// "Acme & Co" keep their internal punctuation.</summary>
    private static readonly char[] TrailingPunctuation = ['.', ',', ';', ':'];

    public static FabricationResult Verify(
        IEnumerable<TailoredSection> sections,
        CandidateProfile profile,
        Role role)
        => Verify(sections, profile, role, additionalHaystacks: null, freeRewrite: false);

    /// <summary>
    /// <paramref name="additionalHaystacks"/> extends the corpus the document is
    /// checked against with trusted text the caller guarantees already derives
    /// from the CV. The LLM tailoring pass passes the raw CV text and the
    /// deterministic draft here: the AI may reuse those words (they are facts the
    /// candidate already produced) while the CV stays the sole source of
    /// entities, numbers and skills.
    /// </summary>
    /// <paramref name="freeRewrite"/>
    /// <summary>When the LLM genuinely rewrote the prose, the entity pass must not
    /// reject ordinary professional vocabulary the model introduces while
    /// rephrasing ("sprints", "stakeholders", "deliverables"). Free rewriting is
    /// then a legitimate output, so these common resume nouns are allowed through:
    /// the check still rejects invented numbers, invented companies/institutions,
    /// and skills the CV does not support. Use strict mode (the default) for the
    /// deterministic build, where the fixed template already permits only CV
    /// vocabulary.</summary>
    public static FabricationResult Verify(
        IEnumerable<TailoredSection> sections,
        CandidateProfile profile,
        Role role,
        IEnumerable<string>? additionalHaystacks,
        bool freeRewrite = false)
    {
        var corpus = BuildCorpus(profile);
        if (additionalHaystacks is not null)
        {
            foreach (var extra in additionalHaystacks)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                    corpus = string.Join("\n", corpus, extra);
            }
        }

        var permitted = PermittedJobTerms(role);
        var fabrications = new List<FabricatedEntity>();
        var checked_ = 0;

        foreach (var section in sections)
        {
            // The Target line states what the job is, not what the candidate did,
            // so it is compared against the role instead of the CV.
            var isJobFacts = section.Source == "role";
            var haystack = isJobFacts ? permitted : corpus;

            foreach (var token in Tokenize(section.Body))
            {
                if (IsNoise(token)) continue;
                if (ProseVerbs.Contains(token)) continue;

                var isNumber = char.IsDigit(token[0]);
                var isProperNoun = char.IsUpper(token[0]);

                if (!isNumber && !isProperNoun) continue;

                // A single capital letter is an initial ("A. Okonkwo"), not a claim.
                if (isProperNoun && token.Length == 1) continue;

                if (freeRewrite && isProperNoun && ProseNouns.Contains(token)) continue;

                checked_++;

                if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase)) continue;

                fabrications.Add(new FabricatedEntity(
                    Text: token,
                    Kind: isNumber ? "number" : "entity",
                    Context: Truncate(section.Body, token)));
            }
        }

        // Pass 3: a posting skill may only surface if the candidate has it.
        // Ownership is decided against the whole CV corpus (raw text + structured
        // fields), not just the parsed skill list: "AI" is a real claim if the CV
        // says it, even when the parser didn't file it under Skills. Minor
        // normalizations are accepted (SkillAliases), so "Excel" may stand on
        // "spreadsheets" — but never on nothing.
        var corpusText = corpus;

        foreach (var skill in role.Skills.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (TemplateWords.Contains(skill)) continue;

            var owned = SkillAliases.MentionedIn(corpusText, skill);

            if (owned) continue;

            checked_++;

            // Only a problem if the document actually says it — as a word, not as
            // a substring ("AI" must not match inside "API testing").
            var saidIt = sections.Any(s => SkillAliases.MentionedIn(s.Body, skill));

            if (saidIt)
            {
                fabrications.Add(new FabricatedEntity(
                    Text: skill,
                    Kind: "unsupported_skill",
                    Context: "The posting asks for this skill and it does not appear on your CV, " +
                             "but the tailored document claims it."));
            }
        }

        return new FabricationResult(
            Passed: fabrications.Count == 0,
            ClaimsChecked: checked_,
            Fabrications: fabrications
                .GroupBy(f => (f.Text, f.Kind))
                .Select(g => g.First())
                .ToList());
    }

    /// <summary>
    /// Everything the candidate has actually said, as one searchable blob.
    ///
    /// Includes the structured fields as well as the raw text, because a fact the
    /// user typed in is a legitimate fact even when it is absent from the
    /// uploaded file. Excluding the structured fields would report a user's own
    /// correction as a fabrication.
    /// </summary>
    private static string BuildCorpus(CandidateProfile profile)
    {
        var parts = new List<string> { profile.RawText ?? "" };

        void Add(string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(value); }

        Add(profile.FullName);
        Add(profile.Headline);
        Add(profile.Location);
        Add(profile.Summary);
        Add(profile.DesiredSalary);
        Add(profile.Availability);
        Add(profile.FileName);

        foreach (var s in profile.Skills) Add($"{s.Name} {s.Normalized}");
        foreach (var e in profile.Experiences)
        {
            Add(e.Company); Add(e.Title); Add(e.Highlights);
            Add(e.StartDate); Add(e.EndDate);
        }
        foreach (var e in profile.Education)
        {
            Add(e.School); Add(e.Degree); Add(e.FieldOfStudy);
            Add(e.Details);
            if (e.StartYear is { } sy) Add(sy.ToString());
            if (e.EndYear is { } ey) Add(ey.ToString());
        }
        foreach (var c in profile.Certifications)
        {
            Add(c.Name); Add(c.Issuer);
            if (c.Year is { } cy) Add(cy.ToString());
        }
        foreach (var l in profile.Languages) { Add(l.Name); Add(l.Proficiency); }
        foreach (var l in profile.Links) { Add(l.Label); Add(l.Url); }
        foreach (var t in profile.TargetRoles) Add(t);

        return string.Join("\n", parts);
    }

    /// <summary>Tokens from the role itself, which the document is entitled to
    /// state because they describe the posting rather than the candidate.</summary>
    private static string PermittedJobTerms(Role role) =>
        string.Join("\n", new[] { role.Title, role.Company, role.Field, role.JobType }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static IEnumerable<string> Tokenize(string text) =>
        TokenPattern.Matches(text)
            .Select(m => m.Value.TrimEnd(TrailingPunctuation))
            .Where(t => t.Length > 0);

    /// <summary>Template vocabulary, and anything too short to be a meaningful
    /// claim. Year suffixes ("2" in "C2") are handled by the length floor.</summary>
    private static bool IsNoise(string token) =>
        token.Length < 2 || TemplateWords.Contains(token);

    /// <summary>A short window around the offending token, so the review UI can
    /// show the sentence that failed rather than making the user hunt for it.</summary>
    private static string Truncate(string body, string token)
    {
        var index = body.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return body.Length <= 120 ? body : body[..120];

        var start = Math.Max(0, index - 50);
        var length = Math.Min(body.Length - start, token.Length + 100);
        return $"…{body.Substring(start, length).Trim()}…";
    }
}
