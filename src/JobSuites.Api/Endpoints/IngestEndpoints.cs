using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
///
/// Two runs of the same source must never overlap (docs/ARCHITECTURE.md §11), so
/// the write path takes a Postgres advisory lock keyed on the source. The second
/// run exits rather than fighting the first one over the same rows.
/// </summary>
public static class IngestEndpoints
{
    /// <summary>Consecutive empty runs before a source is called degraded. One
    /// quiet day is normal — a job board goes a day without the kind of posting
    /// you would notice. Three in a row means selectors probably drifted, and a
    /// silent zero has to be distinguishable from a quiet week (§7).</summary>
    private const int ZeroYieldThreshold = 3;

    public static void MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ingest").WithTags("Ingest");

        // The shared key is checked by middleware in Program.cs, in constant time.
        // Declaring it here as well would be documentation, not enforcement.
        group.MapPost("/roles", UpsertRoles).WithName("IngestRoles");
        group.MapPost("/runs", ReportRun).WithName("IngestReportRun");
        group.MapGet("/health", Health).WithName("IngestHealth");
        group.MapGet("/queue", Queue).WithName("IngestQueue");
        group.MapGet("/known-ids", KnownJobIds).WithName("IngestKnownIds");
    }

    /// <summary>How often a target is re-crawled. Once a day matches how often
    /// the board changes: a user's expectations of "new today" are measured in
    /// hours, and polling hourly spends the politeness budget to find the same
    /// postings sooner.</summary>
    private static int IntervalHours =>
        Math.Clamp(
            int.TryParse(
                Environment.GetEnvironmentVariable("INGEST_INTERVAL_HOURS"),
                out var h) ? h : 24,
            1, 168);

    /// <summary>How far back a target's first crawl looks.
    ///
    /// Three months is a product decision, not a technical one: long enough that
    /// a user searching a thin market sees something, short enough that a role
    /// is still worth applying to. Roles still open after months are usually
    /// open because nobody qualified applied, not because they are fresh.</summary>
    private static int FirstCrawlDays =>
        Math.Clamp(
            int.TryParse(
                Environment.GetEnvironmentVariable("INGEST_FIRST_CRAWL_DAYS"),
                out var d) ? d : 90,
            1, 365);

    /// <summary>
    /// What the crawler should fetch next: every target that is due.
    ///
    /// This is the whole of the per-user retrieval design on the API side. The
    /// crawler has no user list of its own — it asks here, gets back the titles
    /// that are due and where each got to last time, and crawls exactly those.
    /// Keeping the user list in the database rather than in the crawl script is
    /// what makes the schedule a timer rather than something that has to be
    /// reconfigured every time a user changes what they are looking for.
    ///
    /// Targets are derived from each profile's <c>TargetRoles</c> on the way
    /// out, so the profile stays the one place a user states an intent, and
    /// editing it needs no separate sync step.
    /// </summary>
    private static async Task<IResult> Queue(
        AppDbContext db,
        CancellationToken ct,
        int limit = 20,
        bool includeDueOnly = true)
    {
        limit = Math.Clamp(limit, 1, 200);

        var profiles = await db.Profiles
            .AsNoTracking()
            .Select(p => new { p.UserId, p.TargetRoles, p.PreferredStates })
            .ToListAsync(ct);

        // One user's targets are crawled one after another, so a user with six
        // target roles cannot take the whole budget and starve everyone else.
        // Oldest-crawled first, so a backlog drains evenly.
        var cutoff = DateTimeOffset.UtcNow.AddHours(-IntervalHours);
        var due = new List<CrawlTarget>();

        foreach (var profile in profiles)
        {
            var wanted = (profile.TargetRoles ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (wanted.Count == 0) continue;

            var existing = await db.CrawlTargets
                .Where(t => t.UserId == profile.UserId)
                .ToDictionaryAsync(t => t.Title, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var title in wanted)
            {
                if (existing.TryGetValue(title, out var target))
                {
                    // A states edit takes effect on the next run rather than
                    // resetting a crawl that is halfway through.
                    target.States = profile.PreferredStates is { Count: > 0 }
                        ? [.. profile.PreferredStates]
                        : [];
                    due.Add(target);
                }
                else
                {
                    var created = new CrawlTarget
                    {
                        UserId = profile.UserId,
                        Title = title,
                        States = profile.PreferredStates is { Count: > 0 } ? [.. profile.PreferredStates] : [],
                    };
                    db.CrawlTargets.Add(created);
                    due.Add(created);
                }
            }
        }

        await db.SaveChangesAsync(ct);

        var selected = (includeDueOnly ? due.Where(t => t.LastCrawledAt is null || t.LastCrawledAt < cutoff) : due)
            .OrderBy(t => t.LastCrawledAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.CreatedAt)
            .Take(limit)
            .ToList();

        return TypedResults.Ok(new
        {
            // The recency window is decided here, not in the crawler, so the rule
            // is one number in one place rather than a default in a script
            // nobody remembers.
            firstCrawlDays = FirstCrawlDays,
            intervalHours = IntervalHours,
            dueCount = selected.Count,
            targets = selected.Select(t => new
            {
                id = t.Id,
                userId = t.UserId,
                title = t.Title,
                states = t.States,
                maxJobs = t.MaxJobsPerRun,
                cursor = t.Cursor,
                // Null on a target's first run, which is what tells the crawler
                // to apply the full recency window rather than a daily delta.
                since = t.LastCrawledAt,
                firstRun = t.LastCrawledAt is null,
                consecutiveEmptyRuns = t.ConsecutiveEmptyRuns,
            }),
        });
    }

    /// <summary>
    /// Job IDs this source has already stored, so a targeted crawl can skip the
    /// detail fetch for them.
    ///
    /// The check has to happen before the fetch, not after: skipping a known job
    /// after downloading it saves nothing but parsing, and the politeness budget
    /// is spent on requests. Shared across all targets, so one user's crawl
    /// warms the board for every other user's next run.
    /// </summary>
    private static async Task<IResult> KnownJobIds(
        AppDbContext db,
        CancellationToken ct,
        string source = "myjobmag",
        int sinceDays = 0)
    {
        var query = db.RolePostings
            .AsNoTracking()
            .Where(p => p.Source == source);

        if (sinceDays > 0)
        {
            var since = DateTimeOffset.UtcNow.AddDays(-sinceDays);
            query = query.Where(p => p.FirstSeenAt >= since);
        }

        var ids = await query
            .OrderByDescending(p => p.FirstSeenAt)
            .Select(p => p.SourceJobId)
            .ToListAsync(ct);

        return TypedResults.Ok(new { source, count = ids.Count, ids });
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

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // One lock per source in the batch, so two boards can ingest
        // concurrently while a second run of the same board stands down.
        var sources = incoming
            .SelectMany(r => r.Postings ?? [])
            .Select(p => p.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A batch that carries no postings at all still needs mutual exclusion,
        // or two such batches would race on the role rows below.
        if (sources.Count == 0) sources.Add("unknown");

        foreach (var source in sources)
        {
            // Raw form, not the interpolated one: a hole in SqlQuery is compiled
            // as a query expression, and a method call there is emitted into the
            // SQL text as a navigation ("s.Value") instead of being bound.
            var acquired = await db.Database
                .SqlQueryRaw<bool>(
                    // EF composes a scalar SqlQuery as SELECT s."Value" FROM (...)
                    // AS s, so the inner column has to be named exactly that,
                    // quoted. An unquoted "value" is folded to lowercase by
                    // Postgres and the lookup fails.
                    """SELECT pg_try_advisory_xact_lock(hashtext({0})) AS "Value" """,
                    LockKey(source))
                .SingleAsync(ct);

            if (acquired) continue;

            await tx.RollbackAsync(ct);

            return TypedResults.Conflict(new
            {
                title = "Ingest already running",
                detail = $"Another ingest is publishing {source} right now. This run exited " +
                         "without writing anything — re-run it once the other finishes.",
                source,
            });
        }

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

        // Roles whose description may have changed, for requirement extraction
        // once the upsert loop has finished.
        var extracted = new List<Role>();

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

            extracted.Add(role);
        }

        var requirementsWritten = await SyncRequirements(extracted, db, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);

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
        //
        // All users, not just the one this crawl targeted: `roles` is shared, so
        // a role found for one user's search can match another's profile, and
        // only the match layer knows. The cost is O(users) per published batch,
        // which is why a per-user crawl publishes in small batches — a single
        // board-wide sweep would be one recompute for the whole set. Worth
        // revisiting if user count grows, since the obvious fix (a deferred
        // refresh queue) is a real piece of work rather than a tweak.
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
            requirementsWritten,
            received = incoming.Count,
        });
    }

    /// <summary>
    /// Records one poll of one source, whether or not it published anything.
    ///
    /// This is the row that makes "nothing new today" distinguishable from "our
    /// selectors are broken". A run that yielded zero roles and threw no error is
    /// the exact failure mode §7 exists to catch, so it has to be recorded as
    /// diligently as a successful one.
    /// </summary>
    private static async Task<IResult> ReportRun(IngestRunReport report, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(report.SourceKey))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sourceKey"] = ["A run must name the adapter it belongs to."],
            });
        }

        var key = report.SourceKey.Trim();

        var source = await db.Sources.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (source is null)
        {
            source = new Source
            {
                Key = key,
                Name = string.IsNullOrWhiteSpace(report.SourceName) ? key : report.SourceName.Trim(),
                BaseUrl = report.BaseUrl?.Trim() ?? "",
            };
            db.Sources.Add(source);
        }

        var status = report.Status?.Trim().ToLowerInvariant() switch
        {
            "blocked" => "blocked",
            "failed" or "degraded" => "degraded",
            _ => "ok",
        };

        var polledAt = report.FinishedAt ?? DateTimeOffset.UtcNow;

        var run = new IngestRun
        {
            Source = source,
            // A run with no start time is a client that did not time it, not a
            // run that began before the epoch. Stamping it with the poll time is
            // honest; a default DateTimeOffset is not.
            StartedAt = report.StartedAt ?? polledAt,
            FinishedAt = report.FinishedAt,
            PostingsSeen = Math.Max(0, report.PostingsSeen),
            RolesPublished = Math.Max(0, report.RolesPublished),
            ProbeDetail = Cut(report.ProbeDetail, 500),
            Error = Cut(report.Error, 1000),
        };

        // Attribution. Resolved by ID when the client gives one, and by
        // (user, title) otherwise, because the crawler may be a different
        // process from the one that last wrote the profile and the title is the
        // part it actually knows. A target that cannot be resolved is not an
        // error: the run is still worth recording for source health.
        if (report.CrawlTargetId is Guid targetId && targetId != Guid.Empty)
        {
            var target = await db.CrawlTargets.FirstOrDefaultAsync(t => t.Id == targetId, ct);
            if (target is not null)
            {
                run.CrawlTarget = target;
                run.UserId = target.UserId;
                run.TargetTitle = target.Title;
            }
        }
        else if (!string.IsNullOrWhiteSpace(report.TargetTitle) && report.UserId is Guid uid && uid != Guid.Empty)
        {
            var title = report.TargetTitle.Trim();
            var target = await db.CrawlTargets.FirstOrDefaultAsync(
                t => t.UserId == uid && t.Title == title, ct);
            if (target is not null)
            {
                run.CrawlTarget = target;
                run.UserId = target.UserId;
                run.TargetTitle = target.Title;
            }
        }

        // Source health is a statement about the *board*, so it is judged only
        // from board-wide sweeps.
        //
        // Counting targeted runs here was wrong in a way that looked healthy and
        // was not: "software engineering" finding no new jobs is an ordinary
        // Tuesday, but it incremented the board's zero-run counter, and after
        // three quiet searches the dashboard declared MyJobMag degraded. The
        // probe had passed, the crawler had been busy, and the only thing wrong
        // was the user's chosen vocabulary. A per-target counter on CrawlTarget
        // is the right home for that signal, and it already exists.
        // The navigation, not `CrawlTargetId`: assigning the navigation does not
        // populate the FK scalar until change tracking runs at SaveChanges, so
        // reading the FK here sees null and every targeted run would be
        // misread as a board-wide sweep.
        var isSweep = run.CrawlTarget is null;

        // An empty but successful sweep is a signal; an empty *failed* run is
        // just a failure. Only the former counts towards the drift threshold.
        var emptySuccess = isSweep && status == "ok" && report.RolesPublished <= 0;

        if (isSweep)
        {
            source.ConsecutiveZeroRuns = emptySuccess ? source.ConsecutiveZeroRuns + 1 : 0;
            source.LastYield = report.RolesPublished;
        }

        source.LastPolledAt = polledAt;
        source.LastSeenAt = polledAt;
        source.Status = status switch
        {
            "blocked" => "blocked",
            "degraded" => "degraded",
            // Healthy unless it has been empty often enough to suspect drift.
            _ => source.ConsecutiveZeroRuns >= ZeroYieldThreshold ? "degraded" : "healthy",
        };

        run.Status = source.Status;

        // A targeted run's yield is a different question from a board-wide
        // sweep's. The sweep finding nothing is normal; a search finding nothing
        // repeatedly is that search's problem, and it needs its own counter
        // because the source-level threshold would be masked by other targets
        // returning plenty.
        var runEmpty = run.RolesPublished <= 0;
        if (run.CrawlTarget is { } served)
        {
            served.ConsecutiveEmptyRuns = status == "ok" && runEmpty
                ? served.ConsecutiveEmptyRuns + 1
                : 0;

            if (status == "ok")
            {
                served.LastCrawledAt = polledAt;
                // The crawler owns the cursor: it reports how far through the
                // sitemap it got, which is the only machine that knows.
                if (report.Cursor is int cursor && cursor >= 0) served.Cursor = cursor;
            }
        }

        db.IngestRuns.Add(run);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new
        {
            source = source.Key,
            status = source.Status,
            consecutiveZeroRuns = source.ConsecutiveZeroRuns,
            lastPolledAt = source.LastPolledAt,
            // Echoed back so a crawler driving many targets can log which one
            // landed without correlating by timestamp.
            target = run.TargetTitle,
            cursor = run.CrawlTarget?.Cursor,
            consecutiveEmptyTargetRuns = run.CrawlTarget?.ConsecutiveEmptyRuns,
        });
    }

    private static async Task<IResult> Health(AppDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles.CountAsync(ct);
        var postings = await db.RolePostings.CountAsync(ct);
        var lastSeen = await db.Roles.MaxAsync(r => (DateTimeOffset?)r.LastSeenAt, ct);
        var requirements = await db.JdRequirements.CountAsync(ct);

        var sources = await db.Sources.AsNoTracking()
            .OrderByDescending(s => s.LastPolledAt)
            .Select(s => new
            {
                s.Key,
                s.Name,
                s.Status,
                s.LastPolledAt,
                s.LastYield,
                s.ConsecutiveZeroRuns,
            })
            .ToListAsync(ct);

        var runs = await db.IngestRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(10)
            .Select(r => new
            {
                r.StartedAt,
                r.FinishedAt,
                r.Status,
                r.PostingsSeen,
                r.RolesPublished,
                r.ProbeDetail,
                r.Error,
                Source = r.Source.Key,
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new
        {
            ok = true,
            roles,
            postings,
            requirements,
            lastSeenAt = lastSeen,
            sources,
            recentRuns = runs,
        });
    }

    /// <summary>
    /// Re-extracts each role's requirements, but only for roles whose description
    /// actually changed.
    ///
    /// The hash is what makes this affordable: a daily poll re-sends every live
    /// role, and re-extracting all of them each time would rewrite the requirement
    /// set of every role every day for no reason. Roles whose JD hash matches keep
    /// their rows untouched and cost one hash comparison.
    /// </summary>
    private static async Task<int> SyncRequirements(
        IReadOnlyCollection<Role> roles,
        AppDbContext db,
        CancellationToken ct)
    {
        if (roles.Count == 0) return 0;

        var hashes = roles.ToDictionary(r => r.Id, r => HashJd(r.Description ?? ""));

        // Projected rather than tracked: these rows are about to be deleted and
        // replaced wholesale, so EF must not hold entities it would try to
        // UPDATE behind our DELETE.
        var existing = await db.JdRequirements
            .Where(q => hashes.Keys.Contains(q.RoleId))
            .Select(q => new { q.Id, q.RoleId, q.JdHash })
            .ToListAsync(ct);

        var byRole = existing.GroupBy(q => q.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(q => q.Id).ToList());

        // A role whose description hash is unchanged keeps its rows. This is the
        // line that makes a daily poll cheap: re-extracting every live role every
        // day would rewrite the requirement set of the whole corpus for nothing.
        var stale = roles
            .Where(r => !byRole.TryGetValue(r.Id, out var ids)
                        || ids.Count == 0
                        || existing.Any(q => q.RoleId == r.Id && q.JdHash != hashes[r.Id]))
            .ToList();

        if (stale.Count == 0) return 0;

        var doomed = stale
            .Where(r => byRole.TryGetValue(r.Id, out _))
            .SelectMany(r => byRole[r.Id])
            .ToList();
        if (doomed.Count > 0)
        {
            await db.JdRequirements
                .Where(q => doomed.Contains(q.Id))
                .ExecuteDeleteAsync(ct);
        }

        var written = 0;

        foreach (var role in stale)
        {
            var requirements = RoleRequirements.FromDescription(role);
            if (!RoleRequirements.IsUsable(requirements)) continue;

            foreach (var requirement in requirements)
            {
                db.JdRequirements.Add(new JdRequirement
                {
                    RoleId = role.Id,
                    Key = Cut(requirement.Key, 300)!,
                    Text = Cut(requirement.Text, 400)!,
                    Span = Cut(requirement.Span ?? requirement.Text, 600) ?? "",
                    Category = requirement.Category,
                    MustHave = requirement.MustHave,
                    YearsMin = requirement.YearsMin,
                    Origin = "deterministic",
                    JdHash = hashes[role.Id],
                });
                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>Namespace for the advisory lock so an unrelated caller using the
    /// same hashtext space cannot collide with us.</summary>
    private static string LockKey(string source) => $"jobsuites:ingest:{source}";

    private static string HashJd(string description) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description))).ToLowerInvariant();

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

/// <summary>
/// One poll of one source, as reported by the adapter.
///
/// `status` is the adapter's own verdict on the run: "ok", "degraded" or
/// "blocked". It is not trusted blindly — the API derives the source's health
/// from it together with the yield, because a run that reports ok having found
/// nothing three times running is not ok.
/// </summary>
public record IngestRunReport(
    string SourceKey,
    string? SourceName,
    string? BaseUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int PostingsSeen,
    int RolesPublished,
    string? Status,
    string? ProbeDetail,
    string? Error,
    /// <summary>Set when this run served one user's target rather than sweeping
    /// the board.</summary>
    Guid? CrawlTargetId,
    Guid? UserId,
    string? TargetTitle,
    /// <summary>How far through the sitemap the crawl got, so the next run for
    /// this target resumes instead of re-reading the head.</summary>
    int? Cursor);
