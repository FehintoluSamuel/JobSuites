using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var user = b.Entity<User>();

        user.HasKey(u => u.Id);
        user.Property(u => u.Id).HasColumnName("id");

        user.Property(u => u.Email).HasColumnName("email").HasMaxLength(320);
        user.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200);
        user.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(120);
        user.Property(u => u.CreatedAt).HasColumnName("created_at");

        // Uniqueness lives in the database. A registration race is two concurrent
        // requests both passing an application-level "does this exist?" check —
        // only a unique index actually prevents the duplicate.
        user.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");

        // Every user-scoped table in this system will carry user_id and be
        // filtered on it. Docs/ARCHITECTURE.md §8: scoping must happen in the
        // query, never in application code.
    }
}
