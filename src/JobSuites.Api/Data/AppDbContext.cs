using System.Text.Json;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobSuites.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<CandidateProfile> Profiles => Set<CandidateProfile>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePosting> RolePostings => Set<RolePosting>();
    public DbSet<RoleMatch> Matches => Set<RoleMatch>();

    /// <summary>Shared options for the JSONB columns. DeterministicPropertyNaming
    /// keeps the column format stable regardless of where the model is built.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static T? Decode<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).HasColumnName("id");
            user.Property(u => u.Email).HasColumnName("email").HasMaxLength(320);
            user.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200);
            user.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(120);
            user.Property(u => u.CreatedAt).HasColumnName("created_at");
            user.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");
        });

        b.Entity<CandidateProfile>(p =>
        {
            p.HasKey(x => x.Id);
            p.ToTable("candidate_profiles");
            p.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260);
            p.Property(x => x.RawText).HasColumnName("raw_text");
            p.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            p.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);
            p.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
            p.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(60);
            p.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
            p.Property(x => x.Headline).HasColumnName("headline").HasMaxLength(300);
            p.Property(x => x.YearsExperience).HasColumnName("years_experience");
            p.Property(x => x.Summary).HasColumnName("summary");
            p.Property(x => x.ProfilePicture).HasColumnName("profile_picture").HasMaxLength(120);
            p.Property(x => x.BannerPicture).HasColumnName("banner_picture").HasMaxLength(120);
            p.Property(x => x.PhotoConsentGiven).HasColumnName("photo_consent");
            p.Property(x => x.DesiredSalary).HasColumnName("desired_salary").HasMaxLength(120);
            p.Property(x => x.Availability).HasColumnName("availability").HasMaxLength(200);
            p.Property(x => x.TargetRoles).HasColumnName("target_roles").HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
            p.Property(x => x.PreferredStates).HasColumnName("preferred_states").HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
            p.Property(x => x.DesiredJobTypes).HasColumnName("desired_job_types").HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
            p.Property(x => x.Skills).HasColumnName("skills").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileSkill>>(v) ?? new List<ProfileSkill>());
            p.Property(x => x.Experiences).HasColumnName("experiences").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileExperience>>(v) ?? new List<ProfileExperience>());
            p.Property(x => x.Education).HasColumnName("education").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileEducation>>(v) ?? new List<ProfileEducation>());
            p.Property(x => x.Certifications).HasColumnName("certifications").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileCertification>>(v) ?? new List<ProfileCertification>());
            p.Property(x => x.Languages).HasColumnName("languages").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileLanguage>>(v) ?? new List<ProfileLanguage>());
            p.Property(x => x.Links).HasColumnName("links").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<ProfileLink>>(v) ?? new List<ProfileLink>());
            p.Property(x => x.CreatedAt).HasColumnName("created_at");
            p.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            p.HasOne<User>()
             .WithOne()
             .HasForeignKey<CandidateProfile>(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            p.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_profiles_user");

            // Re-uploading an unchanged CV should be a no-op, not a re-parse.
            p.HasIndex(x => x.ContentHash).HasDatabaseName("ix_profiles_content_hash");
        });

        b.Entity<Role>(r =>
        {
            r.HasKey(x => x.Id);
            r.ToTable("roles");
            r.Property(x => x.Company).HasColumnName("company").HasMaxLength(300);
            r.Property(x => x.Title).HasColumnName("title").HasMaxLength(300);
            r.Property(x => x.Field).HasColumnName("field").HasMaxLength(200);
            r.Property(x => x.JobType).HasColumnName("job_type").HasMaxLength(200);
            r.Property(x => x.Qualification).HasColumnName("qualification").HasMaxLength(300);
            r.Property(x => x.ExperienceRaw).HasColumnName("experience_raw").HasMaxLength(200);
            r.Property(x => x.MinYears).HasColumnName("min_years");
            r.Property(x => x.MaxYears).HasColumnName("max_years");
            r.Property(x => x.States).HasColumnName("states").HasColumnType("text[]");
            r.Property(x => x.Description).HasColumnName("description");
            r.Property(x => x.Skills).HasColumnName("skills").HasColumnType("text[]");
            r.Property(x => x.ContactEmails).HasColumnName("contact_emails").HasColumnType("text[]");
            r.Property(x => x.PostingCount).HasColumnName("posting_count");
            r.Property(x => x.SalaryEstimate).HasColumnName("salary_estimate").HasMaxLength(120);
            r.Property(x => x.PostedAt).HasColumnName("posted_at");
            r.Property(x => x.DeadlineAt).HasColumnName("deadline_at");
            r.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            r.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");

            // This mirrors the adapter's role_key(): company + title, lower-cased.
            // The database is the last line of defence against the adapter
            // re-emitting a role it already reported.
            r.HasIndex(x => new { x.Company, x.Title })
             .IsUnique()
             .HasDatabaseName("ux_roles_company_title");
        });

        b.Entity<RolePosting>(p =>
        {
            p.HasKey(x => x.Id);
            p.ToTable("role_postings");
            p.Property(x => x.Source).HasColumnName("source").HasMaxLength(60);
            p.Property(x => x.SourceJobId).HasColumnName("source_job_id").HasMaxLength(200);
            p.Property(x => x.Url).HasColumnName("url").HasMaxLength(1000);
            p.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
            p.Property(x => x.State).HasColumnName("state").HasMaxLength(120);
            p.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");

            // The navigation must be named. Left to convention, EF pairs the
            // `Role` navigation with its own shadow FK and the explicit HasOne
            // below silently configures a second, duplicate relationship.
            p.HasOne(x => x.Role)
             .WithMany(r => r.Postings)
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            p.HasIndex(x => new { x.Source, x.SourceJobId })
             .IsUnique()
             .HasDatabaseName("ux_postings_source_job");
            p.HasIndex(x => x.RoleId).HasDatabaseName("ix_postings_role");
        });

        b.Entity<RoleMatch>(m =>
        {
            m.HasKey(x => x.Id);
            m.ToTable("role_matches");
            m.Property(x => x.UserId).HasColumnName("user_id");
            m.Property(x => x.JobId).HasColumnName("role_id");
            m.Property(x => x.ProfileHash).HasColumnName("profile_hash").HasMaxLength(64);
            m.Property(x => x.Score).HasColumnName("score");
            m.Property(x => x.Tier).HasColumnName("tier").HasMaxLength(40);
            m.Property(x => x.Evidence).HasColumnName("evidence").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<List<MatchEvidence>>(v) ?? new List<MatchEvidence>());
            m.Property(x => x.Gaps).HasColumnName("gaps").HasColumnType("text[]");
            m.Property(x => x.ComputedAt).HasColumnName("computed_at");

            // Declaring the User relationship explicitly stops EF inferring a
            // second, shadow-state FK from the bare UserId property.
            m.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            m.HasOne(x => x.Job)
             .WithMany()
             .HasForeignKey(x => x.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            // One current score per user per role. Recomputing replaces the row.
            m.HasIndex(x => new { x.UserId, x.JobId })
             .IsUnique()
             .HasDatabaseName("ux_matches_user_role");
        });
    }
}
