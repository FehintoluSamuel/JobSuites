using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using JobSuites.Api.Prep;
using JobSuites.Api.Tailoring;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class PrepEndpoints
{
    private const int DefaultAptitudeItems = 8;

    public static void MapPrepEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prep").WithTags("Prep").RequireAuthorization();

        group.MapGet("/roles/{roleId:guid}", GetRolePrep).WithName("PrepForRole");
        group.MapGet("/sessions", ListSessions).WithName("PrepSessions");
        group.MapPost("/sessions", StartSession).WithName("PrepStartSession");
        group.MapPost("/sessions/{id:guid}/answers", Answer).WithName("PrepAnswer");
        group.MapPost("/sessions/{id:guid}/finish", Finish).WithName("PrepFinish");
    }

    /// <summary>
    /// The question set for a role, generated on first request and served from
    /// cache thereafter.
    ///
    /// The cache is keyed on the role and the candidate, because the questions
    /// are the join between this posting and this CV — a requirement is a gap
    /// question or a strength question depending on who is asking. Keying on the
    /// role alone would serve the first candidate's gaps to everybody else.
    /// </summary>
    private static async Task<IResult> GetRolePrep(
        Guid roleId,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var role = await db.Roles.AsNoTracking()
            .Include(r => r.Requirements)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

        if (role is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That role is no longer available.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var cached = await db.InterviewPreps.AsNoTracking()
            .FirstOrDefaultAsync(p => p.RoleId == roleId && p.UserId == userId, ct);

        InterviewPrep prep;

        if (cached is not null)
        {
            prep = cached;
        }
        else
        {
            var profile = await db.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);

            if (profile is null)
            {
                return TypedResults.Problem(
                    title: "No CV yet",
                    detail: "Interview questions here are built from the gaps between this role " +
                            "and your CV, so we need a CV first.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var questions = PrepEngine.BuildQuestions(profile, role);

            prep = new InterviewPrep
            {
                RoleId = roleId,
                UserId = userId.Value,
                Questions = questions,
                Topics = questions.Select(q => q.Kind).Distinct().ToList(),
            };

            db.InterviewPreps.Add(prep);

            // Two concurrent first requests from the same user on the same role
            // both see no row here and both try to insert; the unique index turns
            // the loser into a unique violation rather than two caches. Caught and
            // re-read so the caller gets a document instead of a 500.
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.InterviewPreps.Remove(prep);
                db.ChangeTracker.Clear();

                prep = await db.InterviewPreps.AsNoTracking()
                    .FirstAsync(p => p.RoleId == roleId && p.UserId == userId, ct);
            }
        }

        // Coverage is always per user, never cached: it is the join between this
        // person's CV and the posting, and two candidates have different answers.
        var coverage = await BuildCoverageAsync(db, userId.Value, role, ct);

        return TypedResults.Ok(new PrepResponse(
            RoleId: roleId,
            RoleTitle: role.Title,
            RoleCompany: role.Company,
            Questions: prep.Questions,
            Coverage: coverage,
            Uncovered: coverage.Where(c => !c.Covered).Select(c => c.Requirement).ToList(),
            Cached: cached is not null,
            CreatedAt: prep.CreatedAt));
    }

    private static async Task<List<RequirementCoverage>> BuildCoverageAsync(
        AppDbContext db, Guid userId, Role role, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null) return [];

        // Reuses the tailoring engine's fact index so prep and tailoring can never
        // disagree about what "covered" means. If they could, the gap surface in a
        // tailored CV would contradict the questions generated from it.
        return TailoringEngine.BuildCoverageForReview(profile, role);
    }

    private static async Task<IResult> ListSessions(
        AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var sessions = await db.PrepSessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Include(s => s.Role)
            .OrderByDescending(s => s.StartedAt)
            .Take(50)
            .ToListAsync(ct);

        return TypedResults.Ok(sessions.Select(s => new PrepSessionSummary(
            s.Id, s.RoleId, s.Role.Title, s.Role.Company, s.Mode, s.Scores, s.StartedAt, s.FinishedAt))
            .ToList());
    }

    /// <summary>Starts a practice session: gap-driven written practice, or the
    /// timed aptitude bank. Scoring uses the same categorical verdict vocabulary
    /// as matching rather than a percentage, because a percentage implies a
    /// precision the item bank does not have.</summary>
    private static async Task<IResult> StartSession(
        StartSessionRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var mode = (request.Mode ?? "").Trim().ToLowerInvariant();
        if (mode is not ("gap_driven" or "aptitude"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mode"] = ["Mode must be gap_driven or aptitude."],
            });
        }

        var role = await db.Roles.AsNoTracking()
            .Include(r => r.Requirements)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);

        if (role is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That role is no longer available.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var session = new PrepSession
        {
            UserId = userId.Value,
            RoleId = request.RoleId,
            Mode = mode,
        };

        IReadOnlyList<AssessmentItemResponse> items = [];

        if (mode == "aptitude")
        {
            await SeedBankIfEmptyAsync(db, ct);

            var take = Math.Clamp(request.ItemCount ?? DefaultAptitudeItems, 2, 16);

            // A fixed stride through the bank rather than a random sample: a
            // candidate re-sitting the same assessment should not get a materially
            // different paper, and a random draw would make the domain mix
            // unreproducible.
            var bank = AssessmentBank.Items;
            var stride = Math.Max(1, bank.Count / take);
            var picked = Enumerable.Range(0, bank.Count)
                .Where(i => i % stride == 0)
                .Select(i => bank[i])
                .Take(take)
                .ToList();

            items = picked.Select(i => new AssessmentItemResponse(
                i.Id.ToString(), i.Domain, i.Difficulty, i.Prompt, i.Options)).ToList();
        }

        db.PrepSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new PrepSessionResponse(
            session.Id, request.RoleId, role.Title, role.Company, mode, items, [], session.StartedAt, null));
    }

    /// <summary>
    /// Records one answer and returns the verdict.
    ///
    /// For a gap question the verdict is about honesty rather than correctness:
    /// the point of the product here is that the candidate answers the gap from
    /// adjacent experience instead of claiming the missing skill
    /// (docs/PRODUCT.md §7.4), so the response tells them whether their answer
    /// did that.
    /// </summary>
    private static async Task<IResult> Answer(
        Guid id,
        AnswerRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var session = await db.PrepSessions
            .Where(s => s.Id == id && s.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "We do not have that session on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (session.FinishedAt is not null)
        {
            return TypedResults.Problem(
                title: "Session already finished",
                detail: "Start a new session to practise again.",
                statusCode: StatusCodes.Status409Conflict);
        }

        string verdict;
        bool honest;
        AptitudeItemResult? itemResult = null;

        // The answer is keyed on whichever identifier the mode uses, so re-answering
        // the same item updates one row rather than accumulating duplicates.
        var questionKey = session.Mode == "aptitude" ? request.ItemId ?? string.Empty : request.QuestionId ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;

        // Which fields are required is decided by the session's mode, not by
        // anything the client sends, so a client cannot talk its session into
        // being scored the other way round.
        if (session.Mode == "aptitude")
        {
            if (request.ItemId is null || !Guid.TryParse(request.ItemId, out var itemId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["itemId"] = ["Which item are you answering?"],
                });
            }

            var item = await db.AssessmentItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == itemId, ct);

            if (item is null)
            {
                return TypedResults.Problem(
                    title: "Not found",
                    detail: "That question is not in the bank.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (request.SelectedOption is not { } selected || selected < 0 || selected >= item.Options.Count)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["selectedOption"] = ["Choose one of the listed options."],
                });
            }

            var correct = selected == item.AnswerKey;
            verdict = correct ? "Correct" : "Incorrect";
            honest = true;

            itemResult = new AptitudeItemResult(
                item.Id.ToString(), item.Domain, correct, item.AnswerKey, item.WorkedSteps);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.QuestionId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["questionId"] = ["Which question are you answering?"],
                });
            }

            if (body.Length < 10)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["body"] = ["Write a little more so the feedback has something to work with."],
                });
            }

            var prep = await db.InterviewPreps.AsNoTracking()
                .FirstOrDefaultAsync(p => p.RoleId == session.RoleId && p.UserId == userId, ct);

            var question = prep?.Questions
                .FirstOrDefault(q => q.Id == request.QuestionId);

            // An id the cache does not contain is a client bug, not a user error,
            // so the wording says what actually went wrong.
            if (question is null)
            {
                return TypedResults.Problem(
                    title: "Unknown question",
                    detail: "That question is not part of the set for this role. Reload the " +
                            "questions and try again.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            (verdict, honest) = JudgeHonestAnswer(question, body);
        }

        var existing = await db.PrepAnswers
            .Where(a => a.SessionId == session.Id && a.QuestionId == questionKey)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            db.PrepAnswers.Add(new PrepAnswer
            {
                SessionId = session.Id,
                QuestionId = questionKey,
                Body = body,
                Verdict = verdict,
                HonestFramingUsed = honest,
            });
        }
        else
        {
            // Re-answering replaces rather than appends, so the session score is
            // never inflated by answering the same question twice.
            existing.Verdict = verdict;
            existing.HonestFramingUsed = honest;
            existing.Body = body;
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new SubmitAnswerResponse(verdict, honest, itemResult));
    }

    /// <summary>
    /// Judges whether an answer claims the missing skill.
    ///
    /// This is a lexical check, and it is deliberately biased towards letting
    /// suspicious answers through to a human rather than flagging honest ones. A
    /// false accusation costs a candidate confidence in the one feature telling
    /// them not to lie, so the bar to trigger it is a first-person ownership claim
    /// about the exact missing term ("I led an IFRS 15 assessment") rather than
    /// any mention of it — because the honest answer is supposed to name the gap
    /// and then talk about adjacency.
    /// </summary>
    private static (string Verdict, bool Honest) JudgeHonestAnswer(PrepQuestion question, string body)
    {
        if (question.Kind != "gap")
        {
            return ("Answered", true);
        }

        // The term to check against comes from the question, never from parsing
        // Gap. Gap is prose for a human ("No evidence of X in your CV."), and
        // recovering a term from it leaves the sentence's trailing punctuation
        // attached — so "X." is looked for, never matches, and the check silently
        // passes everything. A guardrail that always says yes is worse than none.
        var term = (question.MissingTerm ?? string.Empty).Trim().Trim(' ', '.', ',', ';', ':', '!', '?');

        // No single term to check: a years gap or a general question. Stay quiet
        // rather than accuse on a guess.
        if (term.Length < 3)
        {
            return ("Answered", true);
        }

        // "I did X", "my experience with X", "I have used X" — a claim of having
        // done the thing. Mentioning X in any other framing is not a claim.
        var claims = FirstPersonClaim.IsMatch(body)
                     && body.Contains(term, StringComparison.OrdinalIgnoreCase);

        // Explicitly disclaiming is treated as a strong positive, not a neutral.
        var disclaims = Disclaimer.IsMatch(body);

        if (claims && !disclaims)
        {
            return ("Check this claims the missing skill", false);
        }

        return (disclaims ? "Answered honestly" : "Answered", true);
    }

    private static readonly System.Text.RegularExpressions.Regex FirstPersonClaim = new(
        @"\b(i|we)\s+(led|managed|ran|owned|delivered|built|used|worked|have|had|was|am)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex Disclaimer = new(
        @"\b(i (have not|haven't|do not|don't)|no direct|not (directly )?(familiar|experience|worked)|" +
        @"\bwould need to|have not (done|worked)|adjacent|transferable)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Closes a session and scores it.
    ///
    /// Verdicts reuse the matcher's tiers (Strong / Good / Possible / Stretch)
    /// rather than inventing a percentage scale. The item bank is small and
    /// hand-written, so a number off it would be precision theatre — the tier
    /// tells the candidate whether to practise more without pretending to
    /// measure them.
    /// </summary>
    private static async Task<IResult> Finish(Guid id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var session = await db.PrepSessions
            .Where(s => s.Id == id && s.UserId == userId)
            .Include(s => s.Role)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "We do not have that session on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (session.FinishedAt is not null)
        {
            return TypedResults.Problem(
                title: "Already finished",
                detail: "This session has already been scored.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var answers = await db.PrepAnswers.AsNoTracking()
            .Where(a => a.SessionId == session.Id)
            .ToListAsync(ct);

        if (session.Mode == "aptitude")
        {
            var itemIds = answers
                .Where(a => Guid.TryParse(a.QuestionId, out _))
                .Select(a => Guid.Parse(a.QuestionId))
                .ToList();

            var items = await db.AssessmentItems.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync(ct);

            session.Scores = items
                .GroupBy(i => i.Domain)
                .Select(g =>
                {
                    var attempted = g.Count();
                    var correct = g.Count(i => answers.Any(
                        a => a.QuestionId == i.Id.ToString() && a.Verdict == "Correct"));
                    return new DomainScore(
                        g.Key, correct, attempted, VerdictFor(correct / (double)attempted));
                })
                .OrderBy(d => d.Domain)
                .ToList();
        }
        else
        {
            // Written practice is scored on honesty, not correctness, so the
            // domain summary is the gap questions the candidate faced.
            var flagged = answers.Count(a => !a.HonestFramingUsed);
            var honestAnswers = answers.Count(a => a.HonestFramingUsed);

            session.Scores =
            [
                new DomainScore("Gap questions", honestAnswers, answers.Count,
                    VerdictFor(answers.Count == 0 ? 0 : honestAnswers / (double)answers.Count)),
                // `correct` is the count of answers that did *not* claim the
                // missing skill. Passing the flagged count here inverted the
                // number: the worse the session, the higher it scored.
                new DomainScore("Claims to review", answers.Count - flagged, answers.Count,
                    VerdictFor(answers.Count == 0 ? 1 : (answers.Count - flagged) / (double)answers.Count)),
            ];
        }

        session.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new PrepSessionResponse(
            session.Id, session.RoleId, session.Role.Title, session.Role.Company,
            session.Mode, [], session.Scores, session.StartedAt, session.FinishedAt));
    }

    private static string VerdictFor(double ratio) => ratio switch
    {
        >= 0.85 => "Strong",
        >= 0.6 => "Good",
        >= 0.35 => "Possible",
        _ => "Stretch",
    };

    private static async Task SeedBankIfEmptyAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.AssessmentItems.AnyAsync(ct)) return;

        db.AssessmentItems.AddRange(AssessmentBank.Items);
        await db.SaveChangesAsync(ct);
    }
}
