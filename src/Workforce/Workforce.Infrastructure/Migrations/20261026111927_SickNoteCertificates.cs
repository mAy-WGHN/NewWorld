using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SickNoteCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "certificate_on_file",
                schema: "workforce",
                table: "absences");

            migrationBuilder.AddColumn<string>(
                name: "certificate_status",
                schema: "workforce",
                table: "absences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            // Existing sick notes have no uploaded certificate yet (the former flag only said that one exists).
            migrationBuilder.Sql("UPDATE workforce.absences SET certificate_status = 'Missing' WHERE type = 'Sickness';");

            migrationBuilder.AddColumn<DateOnly>(
                name: "certified_through",
                schema: "workforce",
                table: "absences",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_reminder_at",
                schema: "workforce",
                table: "absences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reminders_sent",
                schema: "workforce",
                table: "absences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "certificate_access_log",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    absence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_access_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificate_documents",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    absence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    certified_from = table.Column<DateOnly>(type: "date", nullable: false),
                    certified_to = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificate_files",
                schema: "workforce",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_files", x => x.document_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absences_type_status_certificate_status",
                schema: "workforce",
                table: "absences",
                columns: new[] { "type", "status", "certificate_status" });

            migrationBuilder.CreateIndex(
                name: "ix_certificate_access_log_absence_id",
                schema: "workforce",
                table: "certificate_access_log",
                column: "absence_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_access_log_at",
                schema: "workforce",
                table: "certificate_access_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_access_log_employee_id",
                schema: "workforce",
                table: "certificate_access_log",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_documents_absence_id",
                schema: "workforce",
                table: "certificate_documents",
                column: "absence_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_documents_employee_id",
                schema: "workforce",
                table: "certificate_documents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_documents_uploaded_by",
                schema: "workforce",
                table: "certificate_documents",
                column: "uploaded_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_access_log",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "certificate_documents",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "certificate_files",
                schema: "workforce");

            migrationBuilder.DropIndex(
                name: "ix_absences_type_status_certificate_status",
                schema: "workforce",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "certificate_status",
                schema: "workforce",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "certified_through",
                schema: "workforce",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "last_reminder_at",
                schema: "workforce",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "reminders_sent",
                schema: "workforce",
                table: "absences");

            migrationBuilder.AddColumn<bool>(
                name: "certificate_on_file",
                schema: "workforce",
                table: "absences",
                type: "boolean",
                nullable: true);
        }
    }
}
