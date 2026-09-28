using System.Security.Claims;
using System.Security.Cryptography;
using JobSuites.Api.Contracts;
using JobSuites.Api.Data;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Auth;

public static class AuthEndpoints
{
    /// <summary>Cost 12 ≈ 250ms on commodity hardware. Raise it as hardware
    /// improves; it is deliberately slow and that is the point.</summary>
    private const int BcryptCost = 12;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            .RequireRateLimiting("auth");

        group.MapPost("/register", Register).WithName("Register");
        group.MapPost("/login", Login).WithName("Login");
        group.MapGet("/me", Me).WithName("Me").RequireAuthorization();
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        AppDbContext db,
        IJwtTokenService tokens,
        CancellationToken ct)
    {
        if (ValidationResults.Invalid(AuthValidation.Validate(request)) is { } invalid)
        {
            return invalid;
        }

        var email = Normalize(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Duplicate();
        }

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptCost),
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent registrations for the same email both pass the
            // check above. The unique index is what actually prevents the
            // duplicate, so a violation here means the email was just taken.
            db.ChangeTracker.Clear();

            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                return Duplicate();
            }

            throw;
        }

        return TypedResults.Created("/api/auth/me", Issue(user, tokens));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AppDbContext db,
        IJwtTokenService tokens,
        CancellationToken ct)
    {
        if (ValidationResults.Invalid(AuthValidation.Validate(request)) is { } invalid)
        {
            return invalid;
        }

        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Verify against a real hash even when the user does not exist, so the
        // response time does not reveal which emails are registered.
        var hash = user?.PasswordHash ?? DummyHash;
        var ok = BCrypt.Net.BCrypt.Verify(request.Password, hash);

        // One message for both "no such user" and "wrong password". Telling the
        // caller which emails have accounts is an account-enumeration oracle.
        if (user is null || !ok)
        {
            return TypedResults.Problem(
                title: "Sign-in failed",
                detail: "Email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TypedResults.Ok(Issue(user, tokens));
    }

    private static IResult Me(ClaimsPrincipal principal) => TypedResults.Ok(new
    {
        id = principal.FindFirstValue(ClaimTypes.NameIdentifier),
        email = principal.FindFirstValue(ClaimTypes.Email),
        name = principal.FindFirstValue(ClaimTypes.Name),
    });

    private static AuthResponse Issue(User u, IJwtTokenService tokens)
    {
        var (token, expires) = tokens.Create(u.Id, u.Email, u.FullName);
        return AuthResponse.From(token, expires, new UserResponse(u.Id, u.Email, u.FullName, u.CreatedAt));
    }

    private static IResult Duplicate() => TypedResults.Problem(
        title: "Account exists",
        detail: "An account with that email already exists. Try signing in instead.",
        statusCode: StatusCodes.Status409Conflict);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>A real BCrypt hash of a value nobody will guess, verified against
    /// on the miss path so failed logins cost the same as successful ones.</summary>
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), BcryptCost);
}

public static class CurrentUserExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
