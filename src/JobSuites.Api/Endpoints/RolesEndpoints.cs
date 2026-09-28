using JobSuites.Api.Data;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

/// <summary>
/// Read-only surface exposing the board's own taxonomy.
///
/// docs/PRODUCT.md §4 says target roles are selected "from the board's own
/// taxonomy" — the role titles the ingest pipeline has actually seen. The
/// profile editor uses this to offer suggestions rather than leaving the user to
/// invent strings that will never match anything.
/// </summary>
public static class RolesEndpoints
{
    public static void MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/roles")
            .WithTags("Roles")
            .RequireAuthorization()
            .MapGet("/titles", GetTitles)
            .WithName("RoleTitles");
    }

    private static async Task<IResult> GetTitles(AppDbContext db, CancellationToken ct)
    {
        var titles = await db.Roles.AsNoTracking()
            .Select(r => r.Title)
            .Distinct()
            .OrderBy(t => t)
            .Take(200)
            .ToListAsync(ct);

        return TypedResults.Ok(new
        {
            titles,
            count = db.Roles.Count(),
        });
    }
}