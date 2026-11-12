using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogbookRatingAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "submitted_on_time",
                schema: "portal",
                table: "logbook_reports",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "logbook_week_stats",
                schema: "portal",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week = table.Column<DateOnly>(type: "date", nullable: false),
                    on_time = table.Column<bool>(type: "boolean", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    escalated = table.Column<bool>(type: "boolean", nullable: false),
                    answered = table.Column<bool>(type: "boolean", nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_week_stats", x => new { x.store_id, x.week });
                });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_week_stats_week",
                schema: "portal",
                table: "logbook_week_stats",
                column: "week");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logbook_week_stats",
                schema: "portal");

            migrationBuilder.DropColumn(
                name: "submitted_on_time",
                schema: "portal",
                table: "logbook_reports");
        }
    }
}
