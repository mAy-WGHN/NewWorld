using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "absences",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    certificate_on_file = table.Column<bool>(type: "boolean", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sync_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    synced_version = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approver_copies",
                schema: "workforce",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    approver_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approver_copies", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                schema: "workforce",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    home_store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                schema: "workforce",
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

            migrationBuilder.CreateTable(
                name: "vacation_entitlements",
                schema: "workforce",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacation_entitlements", x => new { x.employee_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "approval_steps",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    approver_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_steps_approval_requests_approval_request_id",
                        column: x => x.approval_request_id,
                        principalSchema: "workforce",
                        principalTable: "approval_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absences_approval_request_id",
                schema: "workforce",
                table: "absences",
                column: "approval_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_absences_employee_id_from",
                schema: "workforce",
                table: "absences",
                columns: new[] { "employee_id", "from" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_requests_subject_type_subject_id",
                schema: "workforce",
                table: "approval_requests",
                columns: new[] { "subject_type", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_approval_steps_approval_request_id_order",
                schema: "workforce",
                table: "approval_steps",
                columns: new[] { "approval_request_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_home_store_id",
                schema: "workforce",
                table: "people",
                column: "home_store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absences",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "approval_steps",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "approver_copies",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "people",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "stores",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "vacation_entitlements",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "workforce");
        }
    }
}
