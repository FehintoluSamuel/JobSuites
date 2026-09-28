using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileEditableFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "availability",
                table: "candidate_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banner_picture",
                table: "candidate_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certifications",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "desired_job_types",
                table: "candidate_profiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "desired_salary",
                table: "candidate_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "education",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "languages",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "links",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "photo_consent",
                table: "candidate_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "preferred_states",
                table: "candidate_profiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "profile_picture",
                table: "candidate_profiles",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "target_roles",
                table: "candidate_profiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "availability",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "banner_picture",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "certifications",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "desired_job_types",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "desired_salary",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "education",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "languages",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "links",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "photo_consent",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "preferred_states",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "profile_picture",
                table: "candidate_profiles");

            migrationBuilder.DropColumn(
                name: "target_roles",
                table: "candidate_profiles");
        }
    }
}
