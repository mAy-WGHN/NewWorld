using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashRegisterSealForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "seal_attached_by",
                schema: "portal",
                table: "cash_registers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "seal_photo_id",
                schema: "portal",
                table: "cash_registers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attached_by",
                schema: "portal",
                table: "cash_register_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_id",
                schema: "portal",
                table: "cash_register_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_register_photos",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_register_photos", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_register_photos",
                schema: "portal");

            migrationBuilder.DropColumn(
                name: "seal_attached_by",
                schema: "portal",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "seal_photo_id",
                schema: "portal",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "attached_by",
                schema: "portal",
                table: "cash_register_events");

            migrationBuilder.DropColumn(
                name: "photo_id",
                schema: "portal",
                table: "cash_register_events");
        }
    }
}
