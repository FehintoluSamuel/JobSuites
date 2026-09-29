using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class IngestHealthAndJdRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jd_requirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    span = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    must_have = table.Column<bool>(type: "boolean", nullable: false),
                    years_min = table.Column<int>(type: "integer", nullable: true),
                    origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    jd_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jd_requirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_jd_requirements_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_yield = table.Column<int>(type: "integer", nullable: true),
                    consecutive_zero_runs = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ingest_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    postings_seen = table.Column<int>(type: "integer", nullable: false),
                    roles_published = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    probe_detail = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingest_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ingest_runs_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runs_source_started",
                table: "ingest_runs",
                columns: new[] { "SourceId", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jd_requirements_role",
                table: "jd_requirements",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "ux_jd_requirements_role_key",
                table: "jd_requirements",
                columns: new[] { "RoleId", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sources_key",
                table: "sources",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ingest_runs");

            migrationBuilder.DropTable(
                name: "jd_requirements");

            migrationBuilder.DropTable(
                name: "sources");
        }
    }
}
