using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SickNoteKindAndElectronicCertificate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "electronic_certificate",
                schema: "workforce",
                table: "absences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "workforce",
                table: "absences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "electronic_certificate",
                schema: "workforce",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "workforce",
                table: "absences");
        }
    }
}
