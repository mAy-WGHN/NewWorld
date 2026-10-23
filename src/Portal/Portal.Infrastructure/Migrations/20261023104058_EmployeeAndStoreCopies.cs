using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeAndStoreCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "people",
                schema: "portal",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    home_store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "portal",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_number = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.store_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_people_home_store_id",
                schema: "portal",
                table: "people",
                column: "home_store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "people",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "portal");
        }
    }
}
