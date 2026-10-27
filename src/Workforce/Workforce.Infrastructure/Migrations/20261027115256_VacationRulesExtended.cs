using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VacationRulesExtended : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rounding",
                schema: "workforce",
                table: "vacation_rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Up");

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_step",
                schema: "workforce",
                table: "vacation_rules",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<string>(
                name: "waiting_mode",
                schema: "workforce",
                table: "vacation_rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Accrued");

            migrationBuilder.AddColumn<int>(
                name: "waiting_period_months",
                schema: "workforce",
                table: "vacation_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "entitlement_changes",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    annual_days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entitlement_changes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entitlement_changes_employee_id",
                schema: "workforce",
                table: "entitlement_changes",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entitlement_changes",
                schema: "workforce");

            migrationBuilder.DropColumn(
                name: "rounding",
                schema: "workforce",
                table: "vacation_rules");

            migrationBuilder.DropColumn(
                name: "rounding_step",
                schema: "workforce",
                table: "vacation_rules");

            migrationBuilder.DropColumn(
                name: "waiting_mode",
                schema: "workforce",
                table: "vacation_rules");

            migrationBuilder.DropColumn(
                name: "waiting_period_months",
                schema: "workforce",
                table: "vacation_rules");
        }
    }
}
