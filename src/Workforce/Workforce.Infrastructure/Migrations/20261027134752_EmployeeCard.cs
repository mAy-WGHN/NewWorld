using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "personnel_number",
                schema: "workforce",
                table: "people",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_cards",
                schema: "workforce",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    personnel_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    receipt_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    private_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    private_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    business_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    main_store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    function_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    region_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workplace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_relationship_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wage_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supervisor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qtrado_export = table.Column<bool>(type: "boolean", nullable: false),
                    web_time_tracking_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    password_change_required = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_cards", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "personnel_number_series",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personnel_number_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reference_entries",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_cards_main_store_id",
                schema: "workforce",
                table: "employee_cards",
                column: "main_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_cards_personnel_number",
                schema: "workforce",
                table: "employee_cards",
                column: "personnel_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reference_entries_kind_code",
                schema: "workforce",
                table: "reference_entries",
                columns: new[] { "kind", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_cards",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "personnel_number_series",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "reference_entries",
                schema: "workforce");

            migrationBuilder.DropColumn(
                name: "personnel_number",
                schema: "workforce",
                table: "people");
        }
    }
}
