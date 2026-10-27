using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtraVacationDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "entry_date",
                schema: "workforce",
                table: "people",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "extra_vacation_days",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extra_vacation_days", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extra_vacation_days_employee_id",
                schema: "workforce",
                table: "extra_vacation_days",
                column: "employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extra_vacation_days",
                schema: "workforce");

            migrationBuilder.DropColumn(
                name: "entry_date",
                schema: "workforce",
                table: "people");
        }
    }
}
