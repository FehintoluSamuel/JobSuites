using System.Security.Claims;
using JobSuites.Api.Auth;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Endpoints;

public static class SupportEndpoints
{
    private static readonly string[] Categories =
        ["bug", "data_issue", "account", "feature_request", "other"];

    private static readonly string[] Statuses = ["open", "awaiting_user", "resolved", "closed"];

    public static void MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/support").WithTags("Support").RequireAuthorization();

        group.MapGet("/tickets", List).WithName("TicketList");
        group.MapPost("/tickets", Create).WithName("TicketCreate");
        group.MapGet("/tickets/{id:guid}", Get).WithName("TicketGet");
        group.MapPost("/tickets/{id:guid}/messages", AddMessage).WithName("TicketMessage");
    }

    private static async Task<IResult> List(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var tickets = await db.SupportTickets.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Include(t => t.Messages)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return TypedResults.Ok(tickets.Select(t => ToResponse(t, null, userId)).ToList());
    }

    private static async Task<IResult> Get(Guid id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var ticket = await db.SupportTickets.AsNoTracking()
            .Where(t => t.Id == id && t.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That ticket is not on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var messages = await db.SupportMessages.AsNoTracking()
            .Where(m => m.TicketId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(ToResponse(ticket, messages, userId));
    }

    /// <summary>
    /// Raises a ticket.
    ///
    /// The initial body becomes the first message rather than a column on the
    /// ticket, so the thread has one storage model whether it started with a
    /// description or not.
    /// </summary>
    private static async Task<IResult> Create(
        CreateTicketRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var category = (request.Category ?? "").Trim().ToLowerInvariant();
        if (!Categories.Contains(category))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["category"] = [$"Choose one of {string.Join(", ", Categories)}."],
            });
        }

        var subject = (request.Subject ?? "").Trim();
        if (subject.Length is < 3 or > 200)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["subject"] = ["Give a subject between 3 and 200 characters."],
            });
        }

        var body = (request.Body ?? "").Trim();
        if (body.Length is < 10 or > 4000)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["Describe the problem in between 10 and 4000 characters."],
            });
        }

        var ticket = new SupportTicket
        {
            UserId = userId.Value,
            Category = category,
            Subject = subject,
        };

        db.SupportTickets.Add(ticket);
        db.SupportMessages.Add(new SupportMessage
        {
            Ticket = ticket,
            AuthorId = userId.Value,
            Body = body,
        });

        await db.SaveChangesAsync(ct);

        var messages = await db.SupportMessages.AsNoTracking()
            .Where(m => m.TicketId == ticket.Id)
            .ToListAsync(ct);

        return TypedResults.Created($"/api/support/tickets/{ticket.Id}", ToResponse(ticket, messages, userId));
    }

    private static async Task<IResult> AddMessage(
        Guid id,
        AddTicketMessageRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        if (userId is null) return TypedResults.Unauthorized();

        var ticket = await db.SupportTickets
            .Where(t => t.Id == id && t.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
        {
            return TypedResults.Problem(
                title: "Not found",
                detail: "That ticket is not on your account.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (ticket.Status == "closed")
        {
            return TypedResults.Problem(
                title: "Ticket closed",
                detail: "Raise a new ticket if the problem is still happening.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var body = (request.Body ?? "").Trim();
        if (body.Length is < 2 or > 4000)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["Write between 2 and 4000 characters."],
            });
        }

        db.SupportMessages.Add(new SupportMessage
        {
            TicketId = ticket.Id,
            AuthorId = userId.Value,
            Body = body,
        });

        // A user replying to a resolved ticket reopens it rather than needing to
        // ask. Anything else would leave a live problem sitting in a resolved
        // column because the user missed a status control.
        ticket.Status = "open";
        ticket.ResolvedAt = null;

        await db.SaveChangesAsync(ct);

        var messages = await db.SupportMessages.AsNoTracking()
            .Where(m => m.TicketId == ticket.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(ToResponse(ticket, messages, userId));
    }

    private static SupportTicketResponse ToResponse(
        SupportTicket t, IReadOnlyList<SupportMessage>? messages = null, Guid? userId = null)
    {
        var thread = messages ?? t.Messages.OrderBy(m => m.CreatedAt).ToList();

        return new SupportTicketResponse(
            t.Id,
            t.Category,
            t.Subject,
            t.Status,
            t.CreatedAt,
            t.ResolvedAt,
            thread.Select(m => new SupportMessageResponse(
                m.Id,
                m.AuthorId,
                // In v1 every author is the user, because there is no support-side
                // account. The flag is derived rather than stored so the UI can
                // already distinguish the two sides when that changes.
                IsMine: userId is null || m.AuthorId == userId,
                m.Body,
                m.CreatedAt)).ToList());
    }
}
