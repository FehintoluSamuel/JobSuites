using System.ComponentModel.DataAnnotations;

namespace JobSuites.Api.Contracts;

public record RegisterRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [property: Required, StringLength(128, MinimumLength = 10)] string Password);

public record LoginRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(128)] string Password);

public record UserResponse(Guid Id, string Email, string FullName, DateTimeOffset CreatedAt);

public record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserResponse User)
{
    public static AuthResponse From(string token, DateTimeOffset expiresAt, UserResponse user)
        => new(token, expiresAt, user);
}
