using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BcIntegration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absence_sync_states",
                schema: "bc_integration",
                columns: table => new
                {
                    absence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_synced_version = table.Column<long>(type: "bigint", nullable: false),
                    bc_system_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_sync_states", x => x.absence_id);
                });

            migrationBuilder.CreateTable(
                name: "employee_states",
                schema: "bc_integration",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personnel_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    home_store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<string>(type: "text", nullable: true),
                    employment_type = table.Column<string>(type: "text", nullable: true),
                    contracted_hours_per_week = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_states", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "erased_employees",
                schema: "bc_integration",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    erased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erased_employees", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "id_mappings",
                schema: "bc_integration",
                columns: table => new
                {
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bc_system_id = table.Column<Guid>(type: "uuid", nullable: false),
                    internal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_id_mappings", x => new { x.entity_type, x.bc_system_id });
                });

            migrationBuilder.CreateTable(
                name: "store_states",
                schema: "bc_integration",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    street = table.Column<string>(type: "text", nullable: true),
                    postal_code = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    opening_hours_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_states", x => x.store_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_errors",
                schema: "bc_integration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_errors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_watermarks",
                schema: "bc_integration",
                columns: table => new
                {
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    initial_load_completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_watermarks", x => x.entity_type);
                });

            migrationBuilder.CreateIndex(
                name: "ix_id_mappings_internal_id",
                schema: "bc_integration",
                table: "id_mappings",
                column: "internal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_states_store_number",
                schema: "bc_integration",
                table: "store_states",
                column: "store_number");

            migrationBuilder.CreateIndex(
                name: "ix_sync_errors_entity_type_entity_id",
                schema: "bc_integration",
                table: "sync_errors",
                columns: new[] { "entity_type", "entity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_sync_states",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "employee_states",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "erased_employees",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "id_mappings",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "store_states",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "sync_errors",
                schema: "bc_integration");

            migrationBuilder.DropTable(
                name: "sync_watermarks",
                schema: "bc_integration");
        }
    }
}
