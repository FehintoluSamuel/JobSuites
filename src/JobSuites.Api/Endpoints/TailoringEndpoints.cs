using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Models;
using JobSuites.Api.Services;
using JobSuites.Api.Services.Llm;
using JobSuites.Api.Tailoring;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class TailoringEndpoints
{
    public static void MapTailoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tailoring").WithTags("Tailoring").RequireAuthorization();

        group.MapGet("/", List).WithName("TailoringList");
        group.MapPost("/", Generate).WithName("TailoringGenerate");
        group.MapGet("/{id:guid}", Get).WithName("TailoringGet");
        group.MapPost("/{id:guid}/approve", Approve).WithName("TailoringApprove");
    }

    /// <summary>
    /// Builds a tailored CV for one role.
    ///
    /// Generation is a closed set operation over the candidate's own facts. The
    /// cheap, deterministic path (TailoringEngine) always runs. When an LLM is
    /// configured, an optional pass refines the prose afterwards — but it is
    /// handed only the deterministic draft and is discarded unless it re-passes
    /// the fabrication check, so an upstream outage just yields the
    /// deterministic document, never a wrong one.
    /// </summary>
    private static async Task<IResult> Generate(
        CreateTailoringRequest request,
        AppDbContext db,
        LlmTailoringPass llmTailoring,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        if (request.RoleId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roleId"] = ["Choose a role to tailor for."],
            });
        }

        // Both lookups are scoped by user_id in the query, not filtered in
        // application code. A role is only tailorable if the user has a match for
        // it, which is also what stops one user reading another's profile-derived
        // document by guessing an id.
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            return TypedResults.Problem(
                title: "No CV yet",
                detail: "Upload a CV before tailoring. Everything in a tailored document comes " +
                        "from your own CV, so there is nothing to select from yet.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var match = await db.Matches.AsNoTracking()
            .Where(m => m.UserId == userId && m.JobId == request.RoleId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            return TypedResults.Problem(
                title: "Role not on your shortlist",
                detail: "Tailoring is offered for roles on your dashboard, so the document is " +
                        "always about a role you have actually looked at.",
                statusCode: StatusCodes.Status404NotFound);
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

        var build = TailoringEngine.Build(profile, role);

        // Optional LLM pass refines prose for this posting. It operates on the
        // deterministic build's closed fact set and is discarded wholesale unless
        // it survives FabricationCheck (see LlmTailoringPass). The deterministic
        // document is always the safe default.
        var sections = build.Document;
        var diff = build.Diff;
        RejectedAiRewrite? rejected = null;

        var aiRefined = await llmTailoring.TryRewriteAsync(profile, role, build, ct);
        if (aiRefined is not null)
        {
            // Rewritten prose: the model was handed the CV and the deterministic
            // draft, so both are trusted wording sources, and ordinary
            // professional vocabulary it introduces is not treated as a claim.
            // Numbers, employers, names, credentials and unsupported posting
            // skills stay strictly gated.
            var aiVerification = FabricationCheck.Verify(aiRefined, profile, role,
                additionalHaystacks: [profile.RawText ?? "", TailoringEngine.RenderText(build.Document)],
                freeRewrite: true);

            if (aiVerification.Passed)
            {
                sections = aiRefined;
                diff = [.. diff, new TailoredChange(
                    Kind: "reworded",
                    Detail: "Prose refined for this posting by an AI pass. Every fact was " +
                            "re-checked against your CV and nothing outside it was kept.")];
            }
            else
            {
                // The rewrite is not the artifact and never becomes one — the
                // stored document stays the deterministic build, which is what
                // approval reads. What is kept is the accusation and the text
                // that drew it, because a rejection the user cannot read is a
                // verdict with no evidence behind it.
                rejected = new RejectedAiRewrite(aiRefined, aiVerification);
                diff = [.. diff, new TailoredChange(
                    Kind: "rewrite_rejected",
                    Detail: $"An AI pass tried to rewrite this document and claimed " +
                            $"{aiVerification.Fabrications.Count} thing(s) that are not in your CV " +
                            $"({string.Join(", ", aiVerification.Fabrications.Select(f => f.Text).Distinct())}). " +
                            "It was discarded — the document below is the one built only from your own facts.")];
            }
        }

        // The deterministic build is a selection and reordering of CV facts, so it
        // is checked in strict mode against the CV alone.
        var verification = FabricationCheck.Verify(sections, profile, role);

        var latest = await db.TailoredDocuments.AsNoTracking()
            .Where(d => d.UserId == userId && d.RoleId == request.RoleId)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);

        // The role was read with AsNoTracking. Attaching it as Unchanged stops
        // EF from treating it as a new row when the document below references it
        // through the Role navigation — otherwise SaveChanges inserts the role a
        // second time and dies on the PK.
        db.Roles.Attach(role);

        var document = new TailoredDocument
        {
            UserId = userId.Value,
            RoleId = request.RoleId,
            // The response is projected straight off this instance, and it reads
            // Role.Title. Setting only the FK would leave the navigation null and
            // NRE on the first generate call.
            Role = role,
            Version = (latest?.Version ?? 0) + 1,
            Document = sections,
            Diff = diff,
            Coverage = build.Coverage,
            Verification = verification,
            RejectedRewrite = rejected,
            FactRefs = build.FactRefs,
            ProfileHash = ProfileMatchHash.Compute(profile),
            // A clean document is still a draft. Approval is the user's decision
            // and nothing is ever used without it (docs/PRODUCT.md §6.4). A failed
            // check is a stronger state than draft: the user has to resolve an
            // accusation of invention, not just read a draft.
            //
            // A rejected AI rewrite does not put the document in this state: the
            // stored document is the deterministic build and is clean, so it stays
            // approvable. The rejection is surfaced in the diff and in
            // RejectedRewrite for the user to read.
            Status = verification.Passed ? "draft" : "needs_review",
        };

        db.TailoredDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(TailoredDocumentResponse.From(document, document.ProfileHash));
    }

    private static async Task<IResult> List(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var documents = await db.TailoredDocuments.AsNoTracking()
            .Where(d => d.UserId == userId)
            .Include(d => d.Role)
            .ToListAsync(ct);

        // Only the newest version per role: an approved v1 that has since been
        // regenerated is history, and a list that showed both would read as two
        // documents for one role.
        var current = documents
            .GroupBy(d => d.RoleId)
            .Select(g => g.OrderByDescending(d => d.Version).First())
            .OrderByDescending(d => d.CreatedAt)
            .ToList();

        // One hash lookup for the whole list. Passing a hardcoded false here would
        // make the "CV changed since this was generated" warning impossible to see
        // from the list, which is where a user actually looks first.
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var currentHash = profile is null ? null : ProfileMatchHash.Compute(profile);

        return TypedResults.Ok(current.Select(d => new TailoringListItem(
            d.Id,
            d.RoleId,
            d.Role.Title,
            d.Role.Company,
            d.Version,
            d.Status,
            d.Verification.Passed,
            d.Coverage.Count(c => c.MustHave && !c.Covered),
            currentHash is not null && d.ProfileHash != currentHash,
            d.ApprovedAt,
            d.CreatedAt)).ToList());
    }

    private static async Task<IResult> Get(Guid id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var document = await db.TailoredDocuments.AsNoTracking()
            .Where(d => d.Id == id && d.UserId == userId)
            .Include(d => d.Role)
            .FirstOrDefaultAsync(ct);

        if (document is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "We do not have that tailored document on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var profile = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(ct);

        var currentHash = profile is null ? string.Empty : ProfileMatchHash.Compute(profile);

        return TypedResults.Ok(TailoredDocumentResponse.From(document, currentHash));
    }

    /// <summary>
    /// Records the user's decision on a document.
    ///
    /// Approval is refused while a fabrication is unresolved. That is the whole
    /// reason the check exists: a document that is known to contain invented
    /// content must not be able to reach an approved state by a user clicking
    /// through a warning.
    /// </summary>
    private static async Task<IResult> Approve(
        Guid id,
        ApproveTailoringRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var document = await db.TailoredDocuments
            .Where(d => d.Id == id && d.UserId == userId)
            .Include(d => d.Role)
            .FirstOrDefaultAsync(ct);

        if (document is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "We do not have that tailored document on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (request.Approve && !document.Verification.Passed)
        {
            return TypedResults.Problem(
                title: "This document cannot be approved",
                detail: "The fabrication check found content that is not in your CV. Regenerate " +
                        "the document, or correct the profile field it came from, before approving.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Re-approving is idempotent rather than an error: approving twice is a
        // double click, not a mistake, and the timestamp should reflect the first
        // decision rather than moving every time the button is hit.
        document.Status = request.Approve ? "approved" : "rejected";
        if (request.Approve && document.ApprovedAt is null)
        {
            document.ApprovedAt = DateTimeOffset.UtcNow;
        }
        else if (!request.Approve)
        {
            // Rejecting an approved document is how the UI withdraws approval, so
            // leaving the old timestamp behind would claim we approved something
            // the user has since withdrawn.
            document.ApprovedAt = null;
        }

        await db.SaveChangesAsync(ct);

        // Staleness is measured against the profile as it is now, not against the
        // hash stored on the document — comparing the document to itself always
        // reports "fresh" and would hide exactly the case staleness exists for.
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var currentHash = profile is null ? document.ProfileHash : ProfileMatchHash.Compute(profile);

        return TypedResults.Ok(TailoredDocumentResponse.From(document, currentHash));
    }
}
