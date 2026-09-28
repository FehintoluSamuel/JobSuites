using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobSuites.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeProfileJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Profiles created before ProfileEditableFields were backfilled with
            // an empty-object value ('{}') for the jsonb list columns. The app
            // reads those as List<T>, which cannot deserialize from an object,
            // so an existing CV profile crashed every dashboard read. Normalise
            // stored values to a JSON array and give the columns a default that
            // parses as one, so future inserts are safe even when the app does
            // not write the column explicitly.
            migrationBuilder.Sql("""
                UPDATE candidate_profiles
                   SET education = '[]'::jsonb,
                       certifications = '[]'::jsonb,
                       languages = '[]'::jsonb,
                       links = '[]'::jsonb
                 WHERE education::text NOT LIKE '[%'
                    OR certifications::text NOT LIKE '[%'
                    OR languages::text NOT LIKE '[%'
                    OR links::text NOT LIKE '[%'
                """);

            migrationBuilder.AlterColumn<string>(
                name: "education",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "certifications",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "languages",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "links",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the migration this corrects, for rollback symmetry.
            migrationBuilder.AlterColumn<string>(
                name: "education",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "certifications",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "languages",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "links",
                table: "candidate_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");
        }
    }
}