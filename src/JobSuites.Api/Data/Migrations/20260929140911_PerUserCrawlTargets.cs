using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerUserCrawlTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "crawl_target_id",
                table: "ingest_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_title",
                table: "ingest_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "ingest_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "crawl_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    states = table.Column<string>(type: "jsonb", nullable: false),
                    max_jobs_per_run = table.Column<int>(type: "integer", nullable: false),
                    last_crawled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cursor = table.Column<int>(type: "integer", nullable: false),
                    consecutive_empty_runs = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crawl_targets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ingest_runs_crawl_target_id",
                table: "ingest_runs",
                column: "crawl_target_id");

            migrationBuilder.CreateIndex(
                name: "ix_runs_user_started",
                table: "ingest_runs",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_targets_last_crawled",
                table: "crawl_targets",
                column: "last_crawled_at");

            migrationBuilder.CreateIndex(
                name: "ux_crawl_targets_user_title",
                table: "crawl_targets",
                columns: new[] { "user_id", "title" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ingest_runs_crawl_targets_crawl_target_id",
                table: "ingest_runs",
                column: "crawl_target_id",
                principalTable: "crawl_targets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ingest_runs_crawl_targets_crawl_target_id",
                table: "ingest_runs");

            migrationBuilder.DropTable(
                name: "crawl_targets");

            migrationBuilder.DropIndex(
                name: "IX_ingest_runs_crawl_target_id",
                table: "ingest_runs");

            migrationBuilder.DropIndex(
                name: "ix_runs_user_started",
                table: "ingest_runs");

            migrationBuilder.DropColumn(
                name: "crawl_target_id",
                table: "ingest_runs");

            migrationBuilder.DropColumn(
                name: "target_title",
                table: "ingest_runs");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "ingest_runs");
        }
    }
}
