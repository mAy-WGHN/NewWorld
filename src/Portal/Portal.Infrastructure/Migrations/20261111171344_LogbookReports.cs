using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogbookReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logbook_reports",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week = table.Column<DateOnly>(type: "date", nullable: false),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revised_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_reports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_reports_store_id_week",
                schema: "portal",
                table: "logbook_reports",
                columns: new[] { "store_id", "week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_logbook_reports_week",
                schema: "portal",
                table: "logbook_reports",
                column: "week");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logbook_reports",
                schema: "portal");
        }
    }
}
