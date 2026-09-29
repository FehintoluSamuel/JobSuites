using System.Text.RegularExpressions;
using JobSuites.Api.Matching;
using JobSuites.Api.Tailoring;
using JobSuites.Api.Models;

namespace JobSuites.Api.Prep;

/// <summary>
/// Builds the questions a candidate is actually likely to be asked about one
/// role, given what their CV does and does not show.
///
/// The value is in the "does not show". A generic prep site has the job
/// description; this has the job description, the CV, and the computed gap
/// between them, which is the only thing that makes a question specific
/// (docs/PRODUCT.md §7.1). Questions are keyed to the role and cached, so the
/// cost is paid once and every user with the same gaps is served from it.
///
/// Every generated question carries an <c>HonestFraming</c> that instructs the
/// candidate to answer from adjacent experience and to state the gap plainly.
/// docs/PRODUCT.md §7.4 treats this as a hard constraint rather than a style
/// preference: a candidate coached into claiming experience they do not have
/// loses the interview, and the product with it.
/// </summary>
public static class PrepEngine
{
    private const int MaxGapQuestions = 8;

    public static List<PrepQuestion> BuildQuestions(CandidateProfile profile, Role role)
    {
        // Classification is delegated to the tailoring engine so the gap surface
        // in a tailored CV and the questions generated here can never disagree.
        var classified = TailoringEngine.ClassifyForPrep(profile, role);
        var facts = CoverageIndex.From(profile, classified.Select(c => c.Requirement).ToList());

        var questions = new List<PrepQuestion>();
        var n = 0;

        // Gaps first, because they are what the interview will probe.
        foreach (var (requirement, supported) in classified)
        {
            if (questions.Count >= MaxGapQuestions) break;
            if (supported) continue;

            if (BuildGapQuestion(requirement, facts, ref n) is { } q) questions.Add(q);
        }

        // Then what the candidate can actually lean on, so prep is not entirely
        // bad news and the session ends on something they own.
        foreach (var (requirement, supported) in classified)
        {
            if (questions.Count >= MaxGapQuestions + 2) break;
            if (!supported) continue;
            if (requirement.Category != "skill") continue;

            n++;
            questions.Add(new PrepQuestion(
                Id: $"s{n}",
                Kind: "strength",
                Prompt: $"This role wants {requirement.Text}. Give me a specific example where " +
                        "you used it and say what changed as a result.",
                Gap: "None — this is a strength on your CV.",
                HonestFraming:
                    "Answer with one concrete project, not a list of tools. State the problem, " +
                    "what you personally did, and the measurable outcome. If the result was not " +
                    "measured, say what you observed instead of inventing a percentage.",
                AdjacentEvidence: requirement.Text));
        }

        if (questions.Count == 0)
        {
            n++;
            questions.Add(new PrepQuestion(
                Id: $"g{n}",
                Kind: "general",
                Prompt: $"Tell me about the work that makes you a fit for {role.Title}.",
                Gap: "We could not match specific requirements — the posting is unusually vague.",
                HonestFraming:
                    "Use only what is on your CV. If the posting is too vague to answer against, " +
                    "say so and ask what the team actually needs; that is a reasonable answer."));
        }

        return questions;
    }

    private static PrepQuestion? BuildGapQuestion(
        RoleRequirement requirement,
        CoverageIndex facts,
        ref int counter)
    {
        counter++;

        return requirement.Category switch
        {
            "skill" => new PrepQuestion(
                Id: $"g{counter}",
                Kind: "gap",
                Prompt: $"This role asks for {requirement.Text}, which is not on your CV. " +
                        "Walk me through the closest work you have done that is relevant to it.",
                Gap: $"No evidence of {requirement.Text} in your CV.",
                HonestFraming:
                    $"Do not claim {requirement.Text} experience — that is the single easiest way " +
                    $"to lose this interview. Instead: name the adjacent work, explain which part " +
                    $"of it transfers, and say what you would do differently to get to " +
                    $"{requirement.Text} in your first 90 days.",
                AdjacentEvidence: facts.DescribeClosestDomain(requirement.Text),
                MissingTerm: requirement.Text),

            "education" => new PrepQuestion(
                Id: $"g{counter}",
                Kind: "gap",
                Prompt: $"The posting asks for {requirement.Text}. What is your closest " +
                        "qualification, and how would you close that gap?",
                Gap: $"Your CV does not show {requirement.Text}.",
                HonestFraming:
                    "State the qualification you do hold and be straightforward that it is not the " +
                    "one asked for. Then show the route: relevant coursework, on-the-job exposure, " +
                    "or a plan to sit the certification. Do not imply you hold a qualification " +
                    "you do not.",
                MissingTerm: requirement.Text),

            // Deliberately no MissingTerm. A years gap is a range, not a noun, and
            // there is no single phrase whose presence in an answer proves the
            // number was inflated. Guessing here would produce the false
            // accusation the check is designed to avoid, so the framing stands on
            // its own and the answer is left unscored rather than wrongly marked.
            "experience" => new PrepQuestion(
                Id: $"g{counter}",
                Kind: "gap",
                Prompt: $"This role asks for {requirement.Text}. Your CV shows less. " +
                        "How would you make the case for yourself?",
                Gap: facts.YearsDescription(requirement.YearsMin),
                HonestFraming:
                    "Lead with the depth and scope you do have rather than trying to round your " +
                    "years up. Name the roles where you owned the outcome, and say plainly that you " +
                    "are short on years but not on responsibility. Never inflate the number."),

            _ => null,
        };
    }

    /// <summary>Flattened view of the profile used to describe a gap in terms the
    /// candidate can act on.</summary>
    private sealed class CoverageIndex
    {
        private readonly List<ProfileSkill> _skills;
        private readonly int? _years;
        private readonly List<ProfileExperience> _experiences;

        private CoverageIndex(
            List<ProfileSkill> skills,
            int? years,
            List<ProfileExperience> experiences)
        {
            _skills = skills;
            _years = years;
            _experiences = experiences;
        }

        public static CoverageIndex From(CandidateProfile profile, List<RoleRequirement> requirements)
        {
            // A gap is only meaningful next to something the candidate does have,
            // so the index carries their whole skill list, not just the
            // requirements it was built with.
            return new CoverageIndex(profile.Skills, profile.YearsExperience, profile.Experiences);
        }

        public string YearsDescription(int? needed) =>
            _years is null
                ? "We could not read your years of experience from your CV."
                : $"Your CV shows {_years} year{(_years == 1 ? "" : "s")} against {needed} asked for.";

        /// <summary>
        /// The nearest thing the candidate does have to the missing requirement.
        /// Sharing tokens is crude but it is deterministic, and a wrong-but-adjacent
        /// suggestion is still a better prompt than nothing — the candidate
        /// recognises the connection, or they do not, and either way nothing false
        /// is asserted.
        /// </summary>
        public string? DescribeClosestDomain(string missing)
        {
            var missingTokens = Tokens(missing);
            if (missingTokens.Count == 0) return null;

            var best = _skills
                .Select(s => (Skill: s, Overlap: missingTokens.Count(t =>
                    Tokens(s.Name).Contains(t, StringComparer.OrdinalIgnoreCase))))
                .Where(x => x.Overlap > 0)
                .OrderByDescending(x => x.Overlap)
                .FirstOrDefault();

            if (best.Skill is null)
            {
                // No lexical overlap. Fall back to the most substantial recent role,
                // which is the honest thing to point at.
                var role = _experiences.FirstOrDefault(e => e.IsCurrent) ?? _experiences.FirstOrDefault();
                return role is null
                    ? null
                    : $"{role.Title} at {role.Company}";
            }

            var years = best.Skill.Years > 0 ? $", {best.Skill.Years} years" : string.Empty;
            return $"{best.Skill.Name}{years}";
        }

        private static List<string> Tokens(string value) =>
            Regex.Matches(value.ToLowerInvariant(), @"[a-z][a-z0-9+#]{2,}")
                .Select(m => m.Value)
                .Distinct()
                .ToList();
    }
}
