using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NumberSeriesRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "end_number",
                schema: "workforce",
                table: "personnel_number_series",
                type: "bigint",
                nullable: false,
                defaultValue: 99999L);

            migrationBuilder.AddColumn<long>(
                name: "start_number",
                schema: "workforce",
                table: "personnel_number_series",
                type: "bigint",
                nullable: false,
                defaultValue: 1001L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_number",
                schema: "workforce",
                table: "personnel_number_series");

            migrationBuilder.DropColumn(
                name: "start_number",
                schema: "workforce",
                table: "personnel_number_series");
        }
    }
}
