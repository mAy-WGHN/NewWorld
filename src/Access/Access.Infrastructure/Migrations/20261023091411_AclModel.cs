using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Access.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AclModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unrestricted = table.Column<bool>(type: "boolean", nullable: false),
                    include_home_store = table.Column<bool>(type: "boolean", nullable: false),
                    scope_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_entries",
                schema: "access",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    owner = table.Column<string>(type: "text", nullable: false),
                    scopable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_entries", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "people",
                schema: "access",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personnel_number = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    home_store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<string>(type: "text", nullable: true),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "access",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => new { x.employee_id, x.kind });
                });

            migrationBuilder.CreateTable(
                name: "responsibilities",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "text", nullable: false),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_responsibilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    privileges = table.Column<List<string>>(type: "text[]", nullable: false),
                    built_in = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scopes",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    store_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "access",
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
                name: "ix_assignments_employee_id",
                schema: "access",
                table: "assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_role_id",
                schema: "access",
                table: "assignments",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_entries_owner",
                schema: "access",
                table: "catalog_entries",
                column: "owner");

            migrationBuilder.CreateIndex(
                name: "ix_people_home_store_id",
                schema: "access",
                table: "people",
                column: "home_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_personnel_number",
                schema: "access",
                table: "people",
                column: "personnel_number");

            migrationBuilder.CreateIndex(
                name: "ix_responsibilities_responsible_employee_id",
                schema: "access",
                table: "responsibilities",
                column: "responsible_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_responsibilities_target_kind_target_id",
                schema: "access",
                table: "responsibilities",
                columns: new[] { "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                schema: "access",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scopes_name",
                schema: "access",
                table: "scopes",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignments",
                schema: "access");

            migrationBuilder.DropTable(
                name: "catalog_entries",
                schema: "access");

            migrationBuilder.DropTable(
                name: "people",
                schema: "access");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "access");

            migrationBuilder.DropTable(
                name: "responsibilities",
                schema: "access");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "access");

            migrationBuilder.DropTable(
                name: "scopes",
                schema: "access");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "access");
        }
    }
}
