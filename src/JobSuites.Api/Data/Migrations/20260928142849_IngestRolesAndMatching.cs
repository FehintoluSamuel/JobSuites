using System;
using System.Collections.Generic;
using JobSuites.Api.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IngestRolesAndMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    headline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    years_experience = table.Column<int>(type: "integer", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    skills = table.Column<List<ProfileSkill>>(type: "jsonb", nullable: false),
                    experiences = table.Column<List<ProfileExperience>>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidate_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_candidate_profiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    company = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    field = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    job_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    qualification = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    experience_raw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    min_years = table.Column<int>(type: "integer", nullable: true),
                    max_years = table.Column<int>(type: "integer", nullable: true),
                    states = table.Column<List<string>>(type: "text[]", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    skills = table.Column<List<string>>(type: "text[]", nullable: false),
                    contact_emails = table.Column<List<string>>(type: "text[]", nullable: false),
                    posting_count = table.Column<int>(type: "integer", nullable: false),
                    salary_estimate = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role_matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    tier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    evidence = table.Column<List<MatchEvidence>>(type: "jsonb", nullable: false),
                    gaps = table.Column<List<string>>(type: "text[]", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_matches_Users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_matches_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_job_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_postings_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_content_hash",
                table: "candidate_profiles",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ux_profiles_user",
                table: "candidate_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_matches_role_id",
                table: "role_matches",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_matches_user_role",
                table: "role_matches",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_postings_role",
                table: "role_postings",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "ux_postings_source_job",
                table: "role_postings",
                columns: new[] { "source", "source_job_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_roles_company_title",
                table: "roles",
                columns: new[] { "company", "title" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_profiles");

            migrationBuilder.DropTable(
                name: "role_matches");

            migrationBuilder.DropTable(
                name: "role_postings");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
