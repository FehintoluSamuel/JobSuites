using System.Text.Json;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobSuites.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<CandidateProfile> Profiles => Set<CandidateProfile>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePosting> RolePostings => Set<RolePosting>();
    public DbSet<RoleMatch> Matches => Set<RoleMatch>();
    public DbSet<TailoredDocument> TailoredDocuments => Set<TailoredDocument>();
    public DbSet<InterviewPrep> InterviewPreps => Set<InterviewPrep>();
    public DbSet<PrepSession> PrepSessions => Set<PrepSession>();
    public DbSet<PrepAnswer> PrepAnswers => Set<PrepAnswer>();
    public DbSet<AssessmentItem> AssessmentItems => Set<AssessmentItem>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();
    public DbSet<JdRequirement> JdRequirements => Set<JdRequirement>();
    public DbSet<CrawlTarget> CrawlTargets => Set<CrawlTarget>();

    /// <summary>Shared options for the JSONB columns. DeterministicPropertyNaming
    /// keeps the column format stable regardless of where the model is built.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static T? Decode<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);

    /// <summary>
    /// Configures a JSONB column holding a list of records.
    ///
    /// The value comparer is the part that is easy to omit and matters: without
    /// one, EF falls back to comparing the CLR objects, which for these
    /// <c>List&lt;T&gt;</c> properties means it can miss an in-place change
    /// (mutating one element and saving) or issue a redundant UPDATE because two
    /// equal lists are different object references. Comparing the serialised form
    /// makes change detection match what is actually stored.
    /// </summary>
    private static void JsonbList<T>(PropertyBuilder<List<T>> property)
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => Decode<List<T>>(v) ?? new List<T>())
            .Metadata
            .SetValueComparer(new JsonbListComparer<T>(Json));
    }

    /// <summary>Compares lists by their serialised form. A JSONB column has no
    /// element identity, so the serialised value is the only honest notion of
    /// "the same". The snapshot is a shallow copy, which is enough because every
    /// record stored in these lists is an immutable positional record — nothing
    /// mutates an element in place, so a deep copy would only cost time.</summary>
    private sealed class JsonbListComparer<T>(JsonSerializerOptions json)
        : ValueComparer<List<T>>(
            (x, y) => JsonSerializer.Serialize(x, json) == JsonSerializer.Serialize(y, json),
            x => JsonSerializer.Serialize(x, json).GetHashCode(),
            x => new List<T>(x))
    {
        public override bool Equals(List<T>? x, List<T>? y) =>
            JsonSerializer.Serialize(x, json) == JsonSerializer.Serialize(y, json);

        public override int GetHashCode(List<T> obj) =>
            JsonSerializer.Serialize(obj, json).GetHashCode();
    }

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
            p.Property(x => x.Skills).HasColumnName("skills");
            JsonbList<ProfileSkill>(p.Property(x => x.Skills));
            p.Property(x => x.Experiences).HasColumnName("experiences");
            JsonbList<ProfileExperience>(p.Property(x => x.Experiences));
            p.Property(x => x.Education).HasColumnName("education");
            JsonbList<ProfileEducation>(p.Property(x => x.Education));
            p.Property(x => x.Certifications).HasColumnName("certifications");
            JsonbList<ProfileCertification>(p.Property(x => x.Certifications));
            p.Property(x => x.Languages).HasColumnName("languages");
            JsonbList<ProfileLanguage>(p.Property(x => x.Languages));
            p.Property(x => x.Links).HasColumnName("links");
            JsonbList<ProfileLink>(p.Property(x => x.Links));
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
            m.Property(x => x.Evidence).HasColumnName("evidence");
            JsonbList<MatchEvidence>(m.Property(x => x.Evidence));
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

        b.Entity<TailoredDocument>(t =>
        {
            t.HasKey(x => x.Id);
            t.ToTable("tailored_documents");
            t.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            t.Property(x => x.ProfileHash).HasColumnName("profile_hash").HasMaxLength(64);
            t.Property(x => x.Document).HasColumnName("document");
            JsonbList<TailoredSection>(t.Property(x => x.Document));
            t.Property(x => x.Diff).HasColumnName("diff");
            JsonbList<TailoredChange>(t.Property(x => x.Diff));
            t.Property(x => x.Coverage).HasColumnName("coverage");
            JsonbList<RequirementCoverage>(t.Property(x => x.Coverage));
            t.Property(x => x.Verification).HasColumnName("verification").HasColumnType("jsonb")
                .HasConversion(v => JsonSerializer.Serialize(v, Json), v => Decode<FabricationResult>(v) ?? FabricationResult.Clean());
            t.Property(x => x.RejectedRewrite).HasColumnName("rejected_rewrite").HasColumnType("jsonb")
                .HasConversion(
                    v => v == null ? null! : JsonSerializer.Serialize(v, Json),
                    v => Decode<RejectedAiRewrite>(v));
            t.Property(x => x.FactRefs).HasColumnName("fact_refs").HasColumnType("text[]");
            t.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            t.Property(x => x.CreatedAt).HasColumnName("created_at");

            t.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            t.HasOne(x => x.Role)
             .WithMany()
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            // Version is allocated per user+role, so the unique index is what
            // guarantees two concurrent generations cannot both claim v2.
            t.HasIndex(x => new { x.UserId, x.RoleId, x.Version })
             .IsUnique()
             .HasDatabaseName("ux_tailored_user_role_version");
        });

        b.Entity<InterviewPrep>(p =>
        {
            p.HasKey(x => x.Id);
            p.ToTable("interview_preps");
            p.Property(x => x.Topics).HasColumnName("topics").HasColumnType("text[]");
            p.Property(x => x.Questions).HasColumnName("questions");
            JsonbList<PrepQuestion>(p.Property(x => x.Questions));
            p.Property(x => x.CreatedAt).HasColumnName("created_at");

            p.HasOne(x => x.Role)
             .WithMany()
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            p.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // The amortisation guarantee in docs/PRODUCT.md §7.1 depends on this,
            // scoped to the candidate because the questions are built from their CV.
            p.HasIndex(x => new { x.RoleId, x.UserId })
             .IsUnique()
             .HasDatabaseName("ux_prep_role_user");
        });

        b.Entity<PrepSession>(s =>
        {
            s.HasKey(x => x.Id);
            s.ToTable("prep_sessions");
            s.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(30);
            s.Property(x => x.Scores).HasColumnName("scores");
            JsonbList<DomainScore>(s.Property(x => x.Scores));
            s.Property(x => x.StartedAt).HasColumnName("started_at");
            s.Property(x => x.FinishedAt).HasColumnName("finished_at");

            s.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            s.HasOne(x => x.Role)
             .WithMany()
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            s.HasIndex(x => x.UserId).HasDatabaseName("ix_prep_sessions_user");
        });

        b.Entity<PrepAnswer>(a =>
        {
            a.HasKey(x => x.Id);
            a.ToTable("prep_answers");
            a.Property(x => x.QuestionId).HasColumnName("question_id").HasMaxLength(80);
            a.Property(x => x.Body).HasColumnName("body");
            a.Property(x => x.Verdict).HasColumnName("verdict").HasMaxLength(40);
            a.Property(x => x.HonestFramingUsed).HasColumnName("honest_framing_used");
            a.Property(x => x.CreatedAt).HasColumnName("created_at");

            a.HasOne(x => x.Session)
             .WithMany()
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);

            // Re-answering a question replaces the previous answer rather than
            // accumulating duplicates that would corrupt the session score.
            a.HasIndex(x => new { x.SessionId, x.QuestionId })
             .IsUnique()
             .HasDatabaseName("ux_prep_answers_session_question");
        });

        b.Entity<AssessmentItem>(i =>
        {
            i.HasKey(x => x.Id);
            i.ToTable("assessment_items");
            i.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(30);
            i.Property(x => x.Difficulty).HasColumnName("difficulty");
            i.Property(x => x.Prompt).HasColumnName("prompt");
            i.Property(x => x.Options).HasColumnName("options");
            JsonbList<string>(i.Property(x => x.Options));
            i.Property(x => x.AnswerKey).HasColumnName("answer_key");
            i.Property(x => x.WorkedSteps).HasColumnName("worked_steps");

            i.HasIndex(x => new { x.Domain, x.Difficulty }).HasDatabaseName("ix_assessment_domain");
        });

        b.Entity<Application>(a =>
        {
            a.HasKey(x => x.Id);
            a.ToTable("applications");
            a.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            a.Property(x => x.Notes).HasColumnName("notes");
            a.Property(x => x.AppliedAt).HasColumnName("applied_at");
            a.Property(x => x.NextActionAt).HasColumnName("next_action_at");
            a.Property(x => x.CreatedAt).HasColumnName("created_at");
            a.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            a.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            a.HasOne(x => x.Role)
             .WithMany()
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            // One record per role, never per posting — docs/PRODUCT.md §8.
            a.HasIndex(x => new { x.UserId, x.RoleId })
             .IsUnique()
             .HasDatabaseName("ux_applications_user_role");
        });

        b.Entity<SupportTicket>(t =>
        {
            t.HasKey(x => x.Id);
            t.ToTable("support_tickets");
            t.Property(x => x.Category).HasColumnName("category").HasMaxLength(30);
            t.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200);
            t.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            t.Property(x => x.CreatedAt).HasColumnName("created_at");
            t.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

            t.HasOne<User>()
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            t.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_tickets_user");
        });

        b.Entity<SupportMessage>(m =>
        {
            m.HasKey(x => x.Id);
            m.ToTable("support_messages");
            m.Property(x => x.Body).HasColumnName("body");
            m.Property(x => x.CreatedAt).HasColumnName("created_at");

            m.HasOne(x => x.Ticket)
             .WithMany()
             .HasForeignKey(x => x.TicketId)
             .OnDelete(DeleteBehavior.Cascade);

            m.HasIndex(x => x.TicketId).HasDatabaseName("ix_messages_ticket");
        });

        b.Entity<Source>(s =>
        {
            s.HasKey(x => x.Id);
            s.ToTable("sources");
            s.Property(x => x.Key).HasColumnName("key").HasMaxLength(60);
            s.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            s.Property(x => x.BaseUrl).HasColumnName("base_url").HasMaxLength(400);
            s.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            s.Property(x => x.LastPolledAt).HasColumnName("last_polled_at");
            s.Property(x => x.LastYield).HasColumnName("last_yield");
            s.Property(x => x.ConsecutiveZeroRuns).HasColumnName("consecutive_zero_runs");
            s.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            s.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");

            // The adapter key is how health accumulates across runs, so it is the
            // natural key. Enforced rather than assumed: two rows for one board
            // would split its history in half and neither half would ever trip
            // the zero-yield warning.
            s.HasIndex(x => x.Key).IsUnique().HasDatabaseName("ux_sources_key");
        });

        b.Entity<IngestRun>(r =>
        {
            r.HasKey(x => x.Id);
            r.ToTable("ingest_runs");
            r.Property(x => x.Status).HasColumnName("status").HasMaxLength(30);
            r.Property(x => x.ProbeDetail).HasColumnName("probe_detail");
            r.Property(x => x.Error).HasColumnName("error");
            r.Property(x => x.StartedAt).HasColumnName("started_at");
            r.Property(x => x.FinishedAt).HasColumnName("finished_at");
            r.Property(x => x.PostingsSeen).HasColumnName("postings_seen");
            r.Property(x => x.RolesPublished).HasColumnName("roles_published");

            r.HasOne(x => x.Source)
             .WithMany(s => s.Runs)
             .HasForeignKey(x => x.SourceId)
             .OnDelete(DeleteBehavior.Cascade);

            // Attribution is optional: a bulk sweep and a per-user run are both
            // legitimate, and a run with no target is not an error. SetNull
            // rather than Cascade, because deleting a target must not delete the
            // evidence that it was ever crawled for.
            r.HasOne(x => x.CrawlTarget)
             .WithMany()
             .HasForeignKey(x => x.CrawlTargetId)
             .OnDelete(DeleteBehavior.SetNull);
            r.Property(x => x.CrawlTargetId).HasColumnName("crawl_target_id");
            r.Property(x => x.UserId).HasColumnName("user_id");
            r.Property(x => x.TargetTitle).HasColumnName("target_title").HasMaxLength(200);

            // Health is read as "the most recent runs for this board", so the
            // index is on the pair rather than on the timestamp alone.
            r.HasIndex(x => new { x.SourceId, x.StartedAt }).HasDatabaseName("ix_runs_source_started");
            // "Which runs served this user's search, newest first" is the
            // question the dashboard asks when a user says they see nothing new.
            r.HasIndex(x => new { x.UserId, x.StartedAt }).HasDatabaseName("ix_runs_user_started");
        });

        b.Entity<CrawlTarget>(t =>
        {
            t.HasKey(x => x.Id);
            t.ToTable("crawl_targets");
            t.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            t.Property(x => x.UserId).HasColumnName("user_id");
            t.Property(x => x.MaxJobsPerRun).HasColumnName("max_jobs_per_run");
            t.Property(x => x.LastCrawledAt).HasColumnName("last_crawled_at");
            t.Property(x => x.Cursor).HasColumnName("cursor");
            t.Property(x => x.ConsecutiveEmptyRuns).HasColumnName("consecutive_empty_runs");
            t.Property(x => x.CreatedAt).HasColumnName("created_at");
            t.Property(x => x.States).HasColumnName("states");
            JsonbList(t.Property(x => x.States));

            // A user editing their target roles to the same text twice must not
            // produce two rows the crawler then works twice.
            t.HasIndex(x => new { x.UserId, x.Title }).IsUnique().HasDatabaseName("ux_crawl_targets_user_title");

            // The queue is "every target due now", so the due-ness is the index.
            t.HasIndex(x => x.LastCrawledAt).HasDatabaseName("ix_crawl_targets_last_crawled");
        });

        b.Entity<JdRequirement>(q =>
        {
            q.HasKey(x => x.Id);
            q.ToTable("jd_requirements");
            q.Property(x => x.Key).HasColumnName("key").HasMaxLength(300);
            q.Property(x => x.Text).HasColumnName("text");
            q.Property(x => x.Span).HasColumnName("span");
            q.Property(x => x.Category).HasColumnName("category").HasMaxLength(40);
            q.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(30);
            q.Property(x => x.JdHash).HasColumnName("jd_hash").HasMaxLength(64);
            q.Property(x => x.YearsMin).HasColumnName("years_min");
            q.Property(x => x.MustHave).HasColumnName("must_have");
            q.Property(x => x.CreatedAt).HasColumnName("created_at");

            // The inverse is named explicitly. Left to convention EF pairs this
            // with a second, shadow relationship against Role.Requirements and
            // creates a duplicate FK column alongside the real one.
            q.HasOne(x => x.Role)
             .WithMany(r => r.Requirements)
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Cascade);

            // Re-extracting a role replaces its requirement set rather than
            // appending to it. Without this, every poll would grow the table and
            // the same requirement would appear three times in the gap surface.
            q.HasIndex(x => new { x.RoleId, x.Key })
             .IsUnique()
             .HasDatabaseName("ux_jd_requirements_role_key");

            // The join reads a role's requirements as one set.
            q.HasIndex(x => x.RoleId).HasDatabaseName("ix_jd_requirements_role");
        });
    }
}
