using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StepUpEntraLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entra_links",
                schema: "access",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_principal_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    entra_object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entra_links", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "step_up_confirmations",
                schema: "access",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_up_confirmations", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "step_up_requests",
                schema: "access",
                columns: table => new
                {
                    state_hash = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nonce = table.Column<string>(type: "text", nullable: false),
                    code_verifier = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_up_requests", x => x.state_hash);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entra_links_entra_object_id",
                schema: "access",
                table: "entra_links",
                column: "entra_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_step_up_requests_expires_at",
                schema: "access",
                table: "step_up_requests",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entra_links",
                schema: "access");

            migrationBuilder.DropTable(
                name: "step_up_confirmations",
                schema: "access");

            migrationBuilder.DropTable(
                name: "step_up_requests",
                schema: "access");
        }
    }
}
