using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogbookReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                schema: "portal",
                table: "logbook_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "escalated_by",
                schema: "portal",
                table: "logbook_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "escalation_note",
                schema: "portal",
                table: "logbook_reports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "logbook_reminder_settings",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    enabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    due_day = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    points_json = table.Column<string>(type: "jsonb", nullable: false),
                    max_reminders = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_reminder_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logbook_reminders",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    point_index = table.Column<int>(type: "integer", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_reminders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_reminders_store_id_week",
                schema: "portal",
                table: "logbook_reminders",
                columns: new[] { "store_id", "week" });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_reminders_store_id_week_point_index",
                schema: "portal",
                table: "logbook_reminders",
                columns: new[] { "store_id", "week", "point_index" },
                unique: true,
                filter: "point_index IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logbook_reminder_settings",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "logbook_reminders",
                schema: "portal");

            migrationBuilder.DropColumn(
                name: "escalated_at",
                schema: "portal",
                table: "logbook_reports");

            migrationBuilder.DropColumn(
                name: "escalated_by",
                schema: "portal",
                table: "logbook_reports");

            migrationBuilder.DropColumn(
                name: "escalation_note",
                schema: "portal",
                table: "logbook_reports");
        }
    }
}
