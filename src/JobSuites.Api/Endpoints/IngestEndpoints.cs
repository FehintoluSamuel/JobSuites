using System.Security.Claims;
using JobSuites.Api.Data;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using JobSuites.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

/// <summary>
/// Write surface for the ingest pipeline. Called by the Python adapter, not by
/// the web app, so it is authenticated with a shared key rather than a user JWT.
///
/// The design goal is that ingest is idempotent: the adapter can re-send the same
/// crawl as many times as it likes without creating duplicates. Uniqueness is
/// enforced by the database, and re-running just refreshes LastSeenAt.
/// </summary>
public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingest").WithTags("Ingest");

        // The shared key is checked by middleware in Program.cs, in constant time.
        // Declaring it here as well would be documentation, not enforcement.
        group.MapPost("/roles", UpsertRoles).WithName("IngestRoles");
        group.MapGet("/health", Health).WithName("IngestHealth");
    }

    private static async Task<IResult> UpsertRoles(
        IReadOnlyCollection<IngestRole> incoming,
        AppDbContext db,
        MatchService matches,
        HttpContext http,
        CancellationToken ct)
    {
        if (incoming.Count == 0)
        {
            return TypedResults.Problem(
                title: "Nothing to ingest",
                detail: "The request body contained no roles.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var now = DateTimeOffset.UtcNow;

        var byKey = await db.Roles
            .Select(r => new { r.Company, r.Title })
            .ToListAsync(ct);

        var existing = byKey.ToDictionary(
            k => NormalizeKey(k.Company, k.Title),
            StringComparer.OrdinalIgnoreCase);

        var companies = incoming.Select(r => r.Company).Distinct().ToList();
        var titles = incoming.Select(r => r.Title).Distinct().ToList();
        var candidates = await db.Roles
            .Where(r => companies.Contains(r.Company) && titles.Contains(r.Title))
            .Include(r => r.Postings)
            .ToListAsync(ct);

        var byKey2 = candidates.ToDictionary(
            r => NormalizeKey(r.Company, r.Title),
            StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var refreshed = 0;
        var postingsAdded = 0;

        foreach (var dto in incoming)
        {
            var key = NormalizeKey(dto.Company, dto.Title);

            if (!byKey2.TryGetValue(key, out var role))
            {
                role = new Role
                {
                    Company = dto.Company,
                    Title = dto.Title,
                    FirstSeenAt = now,
                };
                db.Roles.Add(role);
                byKey2[key] = role;
                created++;
            }
            else
            {
                refreshed++;
            }

            role.Field = Cut(Coalesce(dto.Field, role.Field), 200);
            role.JobType = Cut(Coalesce(dto.JobType, role.JobType), 200);
            role.Qualification = Cut(Coalesce(dto.Qualification, role.Qualification), 300);
            role.ExperienceRaw = Cut(Coalesce(dto.ExperienceRaw, role.ExperienceRaw), 200);
            role.MinYears = dto.MinYears ?? role.MinYears;
            role.MaxYears = dto.MaxYears ?? role.MaxYears;
            role.Description = string.IsNullOrWhiteSpace(dto.Description)
                ? role.Description
                : dto.Description;
            role.SalaryEstimate = Cut(Coalesce(dto.SalaryEstimate, role.SalaryEstimate), 120);
            // Npgsql stores timestamptz as UTC and rejects other offsets. A client
            // that sends a bare date or local time gets normalised here, never
            // bounced.
            role.PostedAt = dto.PostedAt?.ToUniversalTime() ?? role.PostedAt;
            role.DeadlineAt = dto.DeadlineAt?.ToUniversalTime() ?? role.DeadlineAt;
            role.LastSeenAt = now;

            // Union the states rather than replacing: a role posted in Lagos today
            // and in Abuja next week should show both, not just the latest.
            // A client that omits one of these fields sends null rather than an
            // empty list. Treating null as "none stated" beats crashing the whole
            // batch for a single sloppy role.
            role.States = role.States.Union(dto.States ?? [], StringComparer.OrdinalIgnoreCase).ToList();
            role.ContactEmails = role.ContactEmails
                .Union(dto.ContactEmails ?? [], StringComparer.OrdinalIgnoreCase)
                .ToList();

            role.Skills = dto.RoleSkills?.Count > 0
                ? dto.RoleSkills.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => Cut(x, 100) ?? x).ToList()
                : role.Skills.Count > 0
                    ? role.Skills
                    : RoleSkillExtractor.Extract(role);

            foreach (var posting in dto.Postings ?? [])
            {
                var exists = role.Postings.Any(
                    p => p.Source == posting.Source && p.SourceJobId == posting.SourceJobId);

                if (exists) continue;

                role.Postings.Add(new RolePosting
                {
                    RoleId = role.Id,
                    Source = Cut(posting.Source, 60)!,
                    SourceJobId = Cut(posting.SourceJobId, 200)!,
                    Url = Cut(posting.Url, 1000)!,
                    Location = Cut(posting.Location, 200),
                    State = Cut(posting.State, 120),
                    FirstSeenAt = now,
                });
                postingsAdded++;
            }

            role.PostingCount = Math.Max(role.PostingCount, role.Postings.Count);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Only a genuine unique-key collision is retryable. Anything else
            // (a bad value, a constraint) must not hide behind a misleading
            // "concurrent ingest" message, so the healthy next step depends on it.
            var unique = ex.InnerException?.Message is { } msg
                && msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);

            return TypedResults.Conflict(new
            {
                title = unique ? "Concurrent ingest" : "Ingest rejected",
                detail = unique
                    ? "Another ingest wrote the same roles. Re-run to reconcile."
                    : "The batch did not satisfy a database rule and was rolled back. No roles were changed.",
                reason = unique ? null : ex.InnerException?.Message,
            });
        }

        // Ingest changed the role set, so every dashboard that already carried a
        // score is now stale. Recompute matches for every user with a profile.
        var users = await db.Profiles
            .AsNoTracking()
            .Select(p => p.UserId)
            .ToListAsync(ct);

        foreach (var userId in users)
        {
            await matches.RecomputeAsync(userId, ct);
        }

        return TypedResults.Ok(new
        {
            created,
            refreshed,
            postingsAdded,
            received = incoming.Count,
        });
    }

    private static async Task<IResult> Health(AppDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles.CountAsync(ct);
        var postings = await db.RolePostings.CountAsync(ct);
        var lastSeen = await db.Roles.MaxAsync(r => (DateTimeOffset?)r.LastSeenAt, ct);

        return TypedResults.Ok(new
        {
            ok = true,
            roles,
            postings,
            lastSeenAt = lastSeen,
        });
    }

    private static string NormalizeKey(string company, string title) =>
        $"{company.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}";

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Length <= max ? value : value[..max];

    private static string? Coalesce(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
}

public record IngestRole(
    string Company,
    string Title,
    string? Field,
    string? JobType,
    string? Qualification,
    string? ExperienceRaw,
    int? MinYears,
    int? MaxYears,
    string Description,
    IReadOnlyList<string> States,
    IReadOnlyList<string> ContactEmails,
    IReadOnlyList<string> RoleSkills,
    string? SalaryEstimate,
    DateTimeOffset? PostedAt,
    DateTimeOffset? DeadlineAt,
    IReadOnlyList<IngestPosting> Postings);

public record IngestPosting(string Source, string SourceJobId, string Url, string? Location, string? State);
