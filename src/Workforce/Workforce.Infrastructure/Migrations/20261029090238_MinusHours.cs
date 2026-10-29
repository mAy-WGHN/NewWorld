using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MinusHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "book_minus_hours",
                schema: "workforce",
                table: "working_time_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "minus_hours",
                schema: "workforce",
                table: "month_closes",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "book_minus_hours",
                schema: "workforce",
                table: "working_time_rules");

            migrationBuilder.DropColumn(
                name: "minus_hours",
                schema: "workforce",
                table: "month_closes");
        }
    }
}
