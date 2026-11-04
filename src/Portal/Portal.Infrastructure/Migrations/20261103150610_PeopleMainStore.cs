using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeopleMainStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bc_home_store_id",
                schema: "portal",
                table: "people",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "entry_date",
                schema: "portal",
                table: "people",
                type: "date",
                nullable: true);

            // So far the main store was only BC's suggestion.
            migrationBuilder.Sql("UPDATE portal.people SET bc_home_store_id = home_store_id");

            migrationBuilder.CreateTable(
                name: "main_store_copies",
                schema: "portal",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_main_store_copies", x => x.employee_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "main_store_copies",
                schema: "portal");

            migrationBuilder.DropColumn(
                name: "bc_home_store_id",
                schema: "portal",
                table: "people");

            migrationBuilder.DropColumn(
                name: "entry_date",
                schema: "portal",
                table: "people");
        }
    }
}
