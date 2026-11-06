using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure.Audit;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_personnel_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_personnel_number",
                schema: "workforce",
                table: "audit_entries",
                column: "actor_personnel_number");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "workforce",
                table: "audit_entries",
                column: "occurred_at");
            migrationBuilder.Sql(AuditRegistration.AppendOnlySql("workforce"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditRegistration.DropAppendOnlySql("workforce"));
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "workforce");
        }
    }
}
