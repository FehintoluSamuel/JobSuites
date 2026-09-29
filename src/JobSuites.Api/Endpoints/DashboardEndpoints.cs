using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/", Get).WithName("Dashboard");
        group.MapGet("/matches/{roleId:guid}", GetMatch).WithName("MatchDetail");
    }

    /// <summary>
    /// One request for the whole dashboard.
    ///
    /// The web app calls this on load, so it carries the profile, the headline
    /// counts, and the ranked matches. A dashboard that fires five requests and
    /// renders them progressively looks broken on a slow connection; the counts
    /// and the list genuinely belong together.
    /// </summary>
    private static async Task<IResult> Get(
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct,
        int limit = MatchServiceLimits.DefaultLimit,
        string? tier = null)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        limit = Math.Clamp(limit, 1, 100);

        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var rolesIngested = await db.Roles.CountAsync(ct);
        var rolesUpdatedAt = await db.Roles.MaxAsync(r => (DateTimeOffset?)r.LastSeenAt, ct);

        // Read on every dashboard load rather than cached: the whole point is to
        // answer "is this queue fresh, or has ingest quietly stopped?" and a
        // cached answer to that question is worse than no answer.
        var sourceRows = await db.Sources.AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Name, s.Status, s.LastPolledAt, s.LastYield, s.ConsecutiveZeroRuns })
            .ToListAsync(ct);

        var ingest = sourceRows.Count == 0
            ? null
            : new IngestHealth(
                LastPolledAt: sourceRows.Max(s => s.LastPolledAt),
                SourcesTotal: sourceRows.Count,
                SourcesDegraded: sourceRows.Count(s => s.Status != "healthy"),
                ConsecutiveZeroRuns: sourceRows.Max(s => s.ConsecutiveZeroRuns),
                Sources: sourceRows.Select(s => new SourceHealth(
                    s.Key, s.Name, s.Status, s.LastPolledAt, s.LastYield, s.ConsecutiveZeroRuns)).ToList());

        if (profile is null)
        {
            return TypedResults.Ok(new DashboardResponse(
                Profile: null,
                Summary: new DashboardSummary(
                    rolesIngested, 0, 0, 0, 0, 0, rolesUpdatedAt),
                Matches: [],
                HasIngestedRoles: rolesIngested > 0,
                Ingest: ingest));
        }

        IQueryable<RoleMatch> query = db.Matches.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Include(m => m.Job).ThenInclude(r => r.Postings);

        if (!string.IsNullOrWhiteSpace(tier))
        {
            var wanted = tier.Trim();
            query = query.Where(m => m.Tier == wanted);
        }

        var all = await query.ToListAsync(ct);

        var ranked = all
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Job.PostingCount)
            .ThenBy(m => m.Job.Title)
            .ToList();

        var matches = ranked
            .Take(limit)
            .Select(m => ToMatchedRole(m))
            .ToList();

        return TypedResults.Ok(new DashboardResponse(
            Profile: CandidateProfileResponse.From(profile),
            Summary: new DashboardSummary(
                RolesIngested: rolesIngested,
                RolesConsidered: all.Count,
                StrongMatches: all.Count(m => m.Tier == "Strong"),
                GoodMatches: all.Count(m => m.Tier == "Good"),
                PossibleMatches: all.Count(m => m.Tier == "Possible"),
                StretchMatches: all.Count(m => m.Tier == "Stretch"),
                RolesUpdatedAt: rolesUpdatedAt),
            Matches: matches,
            HasIngestedRoles: rolesIngested > 0,
            Ingest: ingest));
    }

    private static async Task<IResult> GetMatch(
        Guid roleId,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var match = await db.Matches.AsNoTracking()
            .Where(m => m.UserId == userId && m.JobId == roleId)
            .Include(m => m.Job).ThenInclude(r => r.Postings)
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "We do not have a match for that role on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToMatchedRole(match));
    }

    private static MatchedRole ToMatchedRole(RoleMatch m) => new(
        RoleId: m.JobId,
        Title: m.Job.Title,
        Company: m.Job.Company,
        Tier: m.Tier,
        Score: m.Score,
        States: m.Job.States,
        RoleSkills: m.Job.Skills,
        SalaryEstimate: m.Job.SalaryEstimate,
        MinYears: m.Job.MinYears,
        MaxYears: m.Job.MaxYears,
        PostingCount: m.Job.PostingCount,
        Description: m.Job.Description,
        Evidence: m.Evidence
            .Select(e => new MatchEvidenceResponse(
                e.Kind, e.Label, e.Detail, e.RolePostingUrl))
            .ToList(),
        Gaps: m.Gaps,
        Postings: m.Job.Postings
            .Select(p => new RolePostingResponse(p.Id, p.Url, p.Location, p.State, p.Source))
            .ToList(),
        PostedAt: m.Job.PostedAt,
        DeadlineAt: m.Job.DeadlineAt);
}

internal static class MatchServiceLimits
{
    public const int DefaultLimit = 25;
}
