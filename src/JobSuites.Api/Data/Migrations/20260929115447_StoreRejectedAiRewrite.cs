using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreRejectedAiRewrite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rejected_rewrite",
                table: "tailored_documents",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rejected_rewrite",
                table: "tailored_documents");
        }
    }
}
