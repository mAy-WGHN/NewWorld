using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrTool_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absence_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paid_time = table.Column<bool>(type: "boolean", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    schedule_symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    set_work_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    weekly_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_contracts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planning_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_patterns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    week_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_patterns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workplaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    store_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cost_center_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    time_account_active = table.Column<bool>(type: "boolean", nullable: false),
                    vacation_threshold = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workplaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    function = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    remark = table.Column<string>(type: "text", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workplace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    planning_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    region_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shift_swap_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_employment_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "employment_contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employees_planning_roles_planning_role_id",
                        column: x => x.planning_role_id,
                        principalTable: "planning_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employees_workplaces_workplace_id",
                        column: x => x.workplace_id,
                        principalTable: "workplaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    workplace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    budget_hours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedules_workplaces_workplace_id",
                        column: x => x.workplace_id,
                        principalTable: "workplaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "absences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    absence_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    time_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    time_to = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    remark = table.Column<string>(type: "text", nullable: true),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    vacation_days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true),
                    confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absences", x => x.id);
                    table.ForeignKey(
                        name: "fk_absences_absence_types_absence_type_id",
                        column: x => x.absence_type_id,
                        principalTable: "absence_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_absences_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cashier_profiles",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    work_relation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    store_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    permission_group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    manager_rights = table.Column<bool>(type: "boolean", nullable: false),
                    interface_profile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    menu_profile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pos_style_profile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_profiles", x => x.employee_id);
                    table.ForeignKey(
                        name: "fk_cashier_profiles_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    shift_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    planning_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    absence_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remark = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_shifts_absence_types_absence_type_id",
                        column: x => x.absence_type_id,
                        principalTable: "absence_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shifts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shifts_planning_roles_planning_role_id",
                        column: x => x.planning_role_id,
                        principalTable: "planning_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_shifts_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absence_types_code",
                table: "absence_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_absences_absence_type_id",
                table: "absences",
                column: "absence_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_absences_employee_id_date_from",
                table: "absences",
                columns: new[] { "employee_id", "date_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_contract_id",
                table: "employees",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employee_no",
                table: "employees",
                column: "employee_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_planning_role_id",
                table: "employees",
                column: "planning_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                table: "employees",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employees_workplace_id",
                table: "employees",
                column: "workplace_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_contracts_code",
                table: "employment_contracts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_roles_code",
                table: "planning_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedules_number",
                table: "schedules",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedules_workplace_id_year_month",
                table: "schedules",
                columns: new[] { "workplace_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_patterns_code",
                table: "shift_patterns",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_absence_type_id",
                table: "shifts",
                column: "absence_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_shifts_employee_id_date",
                table: "shifts",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_shifts_planning_role_id",
                table: "shifts",
                column: "planning_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_shifts_schedule_id_employee_id_date",
                table: "shifts",
                columns: new[] { "schedule_id", "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_workplaces_code",
                table: "workplaces",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workplaces_location_id",
                table: "workplaces",
                column: "location_id",
                unique: true,
                filter: "location_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absences");

            migrationBuilder.DropTable(
                name: "cashier_profiles");

            migrationBuilder.DropTable(
                name: "shift_patterns");

            migrationBuilder.DropTable(
                name: "shifts");

            migrationBuilder.DropTable(
                name: "absence_types");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "schedules");

            migrationBuilder.DropTable(
                name: "employment_contracts");

            migrationBuilder.DropTable(
                name: "planning_roles");

            migrationBuilder.DropTable(
                name: "workplaces");
        }
    }
}
