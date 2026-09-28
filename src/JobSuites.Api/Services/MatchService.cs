using JobSuites.Api.Data;
using JobSuites.Api.Matching;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Services;

/// <summary>
/// Recomputes a user's match scores against the current role set.
///
/// Recompute is a full replace rather than an incremental update, for two
/// reasons: the role set changes underneath us as ingest runs, so a partial
/// update would leave stale scores; and the dashboard is read-heavy and small
/// enough that a full recompute is cheaper to reason about than diffing.
/// </summary>
public sealed class MatchService(AppDbContext db)
{
    public const int DefaultLimit = 25;

    public async Task<RecomputeResult> RecomputeAsync(
        Guid userId,
        CancellationToken ct = default,
        int limit = DefaultLimit)
    {
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null) return new RecomputeResult(0, 0);

        // The reuse key is a digest of the matching inputs, not the raw CV
        // text. An edited skill invalidates the stored scores so they are
        // recomputed; a photo-only edit does not.
        var profileHash = ProfileMatchHash.Compute(profile);

        var roles = await db.Roles.AsNoTracking()
            .Include(r => r.Postings)
            .ToListAsync(ct);

        if (roles.Count == 0) return new RecomputeResult(0, 0);

        // Scores already computed against this exact profile can be kept,
        // provided the role itself has not changed since. This keeps re-uploads
        // and cosmetic edits cheap.
        var existing = await db.Matches
            .Where(m => m.UserId == userId)
            .ToDictionaryAsync(m => m.JobId, ct);

        var lastSeenByRole = roles.ToDictionary(r => r.Id, r => r.LastSeenAt);
        var reusable = existing
            .Where(kv => kv.Value.ProfileHash == profileHash
                      && lastSeenByRole.TryGetValue(kv.Key, out var seen)
                      && kv.Value.ComputedAt >= seen)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var stale = existing.Values
            .Where(m => !reusable.ContainsKey(m.JobId))
            .ToList();

        if (stale.Count > 0) db.Matches.RemoveRange(stale);

        var scored = 0;
        foreach (var role in roles)
        {
            if (reusable.ContainsKey(role.Id)) continue;

            var primaryUrl = role.Postings.FirstOrDefault()?.Url;
            var match = MatchEngine.Compute(profile, role, primaryUrl);

            db.Matches.Add(match);
            scored++;
        }

        await db.SaveChangesAsync(ct);

        var total = await db.Matches.CountAsync(m => m.UserId == userId, ct);
        return new RecomputeResult(total, scored);
    }
}

public record RecomputeResult(int Total, int Scored);
