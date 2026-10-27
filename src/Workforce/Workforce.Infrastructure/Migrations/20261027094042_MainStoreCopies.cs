using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MainStoreCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bc_home_store_id",
                schema: "workforce",
                table: "people",
                type: "uuid",
                nullable: true);

            // Until Access has told us a main store, the existing value is the one from BC.
            migrationBuilder.Sql("UPDATE workforce.people SET bc_home_store_id = home_store_id;");

            migrationBuilder.CreateTable(
                name: "main_store_copies",
                schema: "workforce",
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
                schema: "workforce");

            migrationBuilder.DropColumn(
                name: "bc_home_store_id",
                schema: "workforce",
                table: "people");
        }
    }
}
