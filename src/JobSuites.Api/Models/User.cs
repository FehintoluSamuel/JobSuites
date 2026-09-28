namespace JobSuites.Api.Models;

/// <summary>Account record. Auth only for Sprint 1 — profile fields arrive with the
/// evidence base in Sprint 2 (see docs/PRODUCT.md §4).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stored lower-cased. Uniqueness is enforced by a DB unique index,
    /// not by application code — see AppDbContext.OnModelCreating.</summary>
    public required string Email { get; set; }

    public required string FullName { get; set; }

    /// <summary>BCrypt hash, cost 12. Never returned by any endpoint.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
