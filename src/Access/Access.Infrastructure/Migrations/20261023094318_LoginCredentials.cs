using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LoginCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credentials",
                schema: "access",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pin_hash = table.Column<string>(type: "text", nullable: false),
                    encrypted_pin = table.Column<string>(type: "text", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credentials", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_employee_id",
                schema: "access",
                table: "refresh_tokens",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at",
                schema: "access",
                table: "refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "access",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credentials",
                schema: "access");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "access");
        }
    }
}
