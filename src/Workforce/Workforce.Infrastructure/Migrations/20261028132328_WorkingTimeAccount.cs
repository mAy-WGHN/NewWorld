using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkingTimeAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "month_closes",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    worked_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    threshold_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    overtime_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_month_closes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monthly_thresholds",
                schema: "workforce",
                columns: table => new
                {
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_thresholds", x => x.relationship_id);
                });

            migrationBuilder.CreateTable(
                name: "time_bookings",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    month_close_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_bookings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "working_time_rules",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    payout_threshold_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    expiry_months = table.Column<int>(type: "integer", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_time_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_month_closes_employee_id_year_month",
                schema: "workforce",
                table: "month_closes",
                columns: new[] { "employee_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_bookings_employee_id_date",
                schema: "workforce",
                table: "time_bookings",
                columns: new[] { "employee_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "month_closes",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "monthly_thresholds",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "time_bookings",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "working_time_rules",
                schema: "workforce");
        }
    }
}
