using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class ApplicationEndpoints
{
    /// <summary>The one pipeline the product has, in order. Forward-only: a
    /// candidate can shortlist, apply, interview and close, and cannot quietly
    /// walk a Closed role back to Shortlisted, because that state is a record of
    /// something that happened rather than a label.</summary>
    private static readonly string[] Pipeline = ["Shortlisted", "Applied", "Interviewing", "Closed"];

    public static void MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/applications").WithTags("Applications").RequireAuthorization();

        group.MapGet("/", List).WithName("ApplicationsList");
        group.MapPost("/", Create).WithName("ApplicationsCreate");
        group.MapPatch("/{id:guid}", Update).WithName("ApplicationsUpdate");
    }

    private static async Task<IResult> List(
        AppDbContext db, ClaimsPrincipal principal, CancellationToken ct, string? status = null)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        IQueryable<Application> query = db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.Role).ThenInclude(r => r.Postings);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            if (!Pipeline.Contains(wanted, StringComparer.OrdinalIgnoreCase))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Status must be one of {string.Join(", ", Pipeline)}."],
                });
            }

            query = query.Where(a => a.Status == wanted);
        }

        var all = await query.ToListAsync(ct);

        var (items, nextActions) = await ToResponsesAsync(db, userId.Value, all, ct);

        return TypedResults.Ok(new ApplicationSummary(
            Shortlisted: all.Count(a => a.Status == "Shortlisted"),
            Applied: all.Count(a => a.Status == "Applied"),
            Interviewing: all.Count(a => a.Status == "Interviewing"),
            Closed: all.Count(a => a.Status == "Closed"),
            Total: all.Count,
            Items: items,
            NextActions: nextActions));
    }

    private static async Task<IResult> Create(
        CreateApplicationRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        if (request.RoleId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roleId"] = ["Choose a role to track."],
            });
        }

        var status = NormalizeStatus(request.Status, "Shortlisted");
        if (status is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status must be one of {string.Join(", ", Pipeline)}."],
            });
        }

        var role = await db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, ct);

        if (role is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That role is no longer available.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // One record per role, never per posting — docs/PRODUCT.md §8. Shortlisting
        // the same role from a second state updates the existing record instead of
        // creating a duplicate the user then has to reconcile.
        var existing = await db.Applications
            .Where(a => a.UserId == userId && a.RoleId == request.RoleId)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return TypedResults.Problem(
                title: "Already tracking this role",
                detail: $"This role is already on your tracker as {existing.Status}. " +
                        "One application is tracked per role, not per posting.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var application = new Application
        {
            UserId = userId.Value,
            RoleId = request.RoleId,
            Status = status,
            Notes = Trim(request.Notes, 4000),
            NextActionAt = request.NextActionAt,
        };

        StampAppliedAt(application);

        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);

        var (items, _) = await ToResponsesAsync(db, userId.Value, [application], ct);
        return TypedResults.Created($"/api/applications/{application.Id}", items[0]);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateApplicationRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var application = await db.Applications
            .Where(a => a.Id == id && a.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (application is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That application is not on your tracker.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (request.Status is not null)
        {
            var status = NormalizeStatus(request.Status, application.Status);
            if (status is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Status must be one of {string.Join(", ", Pipeline)}."],
                });
            }

            var from = Array.IndexOf(Pipeline, application.Status);
            var to = Array.IndexOf(Pipeline, status);

            // Forward-only, and never out of Closed. The user can reopen a
            // shortlist they no longer want by deleting nothing — but a Closed
            // record that silently returns to Shortlisted would misrepresent
            // something that actually happened.
            if (to < from || application.Status == "Closed")
            {
                return TypedResults.Problem(
                    title: "That move is not allowed",
                    detail: application.Status == "Closed"
                        ? "This application is closed. Closed is the end of the pipeline."
                        : $"An application moves forward through " +
                          $"{string.Join(" → ", Pipeline)}, not back.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            application.Status = status;
            StampAppliedAt(application);
        }

        if (request.Notes is not null) application.Notes = Trim(request.Notes, 4000);
        if (request.NextActionAt is not null) application.NextActionAt = request.NextActionAt;

        application.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var (items, _) = await ToResponsesAsync(db, userId.Value, [application], ct);
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Sets the date the user says they applied. The system never writes it
    /// itself: the product submits nothing on the user's behalf, so it has no way
    /// to know an application happened (docs/PRODUCT.md §8). Stamping it on the
    /// transition is the honest moment — the user moved the record to Applied, so
    /// that is when they are telling us it happened.
    /// </summary>
    private static void StampAppliedAt(Application application)
    {
        if (application.Status == "Applied" && application.AppliedAt is null)
        {
            application.AppliedAt = DateTimeOffset.UtcNow;
        }
    }

    private static string? NormalizeStatus(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var wanted = raw.Trim();
        return Pipeline.FirstOrDefault(p => string.Equals(p, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    /// <summary>
    /// Projects applications to responses, and pulls the two things a tracker
    /// needs that are not on the application row: the match tier, so the user can
    /// see fit next to progress, and whether a tailored CV exists, so "prepare"
    /// and "track" are visibly the same object.
    /// </summary>
    private static async Task<(List<ApplicationResponse> Items, List<ApplicationResponse> NextActions)>
        ToResponsesAsync(AppDbContext db, Guid userId, List<Application> apps, CancellationToken ct)
    {
        if (apps.Count == 0) return ([], []);

        var roleIds = apps.Select(a => a.RoleId).ToList();

        // The role is loaded here rather than assumed present on the navigation.
        // Create and Update both hand us an entity straight out of the change
        // tracker, where Role was never loaded, and reading a.Role.Title off
        // those nulls out after the row has already been written — the insert
        // succeeds and the request still returns 500.
        var roles = await db.Roles.AsNoTracking()
            .Include(r => r.Postings)
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var tiers = await db.Matches.AsNoTracking()
            .Where(m => m.UserId == userId && roleIds.Contains(m.JobId))
            .ToDictionaryAsync(m => m.JobId, m => m.Tier, ct);

        var tailored = await db.TailoredDocuments.AsNoTracking()
            .Where(d => d.UserId == userId && roleIds.Contains(d.RoleId))
            .Select(d => d.RoleId)
            .Distinct()
            .ToDictionaryAsync(r => r, _ => true, ct);

        var items = apps
            .Where(a => roles.ContainsKey(a.RoleId))
            .Select(a => new ApplicationResponse(
            a.Id,
            a.RoleId,
            roles[a.RoleId].Title,
            roles[a.RoleId].Company,
            roles[a.RoleId].Postings.FirstOrDefault()?.Url ?? "",
            a.Status,
            a.AppliedAt,
            a.Notes,
            a.NextActionAt,
            tiers.GetValueOrDefault(a.RoleId),
            tailored.ContainsKey(a.RoleId),
            a.CreatedAt,
            a.UpdatedAt))
            .ToList();

        // Only open work, soonest first. A next-action date in the past is the
        // most useful thing on this screen, so overdue sorts ahead of everything.
        var nextActions = items
            .Where(i => i.Status != "Closed" && i.NextActionAt is not null)
            .OrderBy(i => i.NextActionAt)
            .Take(5)
            .ToList();

        return (items, nextActions);
    }
}
