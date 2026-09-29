using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrepCachePerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_prep_role",
                table: "interview_preps");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "interview_preps",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_interview_preps_UserId",
                table: "interview_preps",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_prep_role_user",
                table: "interview_preps",
                columns: new[] { "RoleId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_interview_preps_Users_UserId",
                table: "interview_preps",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_interview_preps_Users_UserId",
                table: "interview_preps");

            migrationBuilder.DropIndex(
                name: "IX_interview_preps_UserId",
                table: "interview_preps");

            migrationBuilder.DropIndex(
                name: "ux_prep_role_user",
                table: "interview_preps");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "interview_preps");

            migrationBuilder.CreateIndex(
                name: "ux_prep_role",
                table: "interview_preps",
                column: "RoleId",
                unique: true);
        }
    }
}
