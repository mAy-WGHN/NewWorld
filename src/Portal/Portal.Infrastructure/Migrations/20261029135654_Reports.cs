using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Reports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_recipients",
                schema: "portal",
                columns: table => new
                {
                    category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_recipients", x => new { x.category, x.employee_id });
                });

            migrationBuilder.CreateTable(
                name: "reports",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personnel_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    device = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    noticed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    noticed_how = table.Column<string>(type: "text", nullable: true),
                    prior_activity = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    parent_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_locations",
                schema: "portal",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_locations", x => new { x.report_id, x.location_id });
                    table.ForeignKey(
                        name: "fk_report_locations_reports_report_id",
                        column: x => x.report_id,
                        principalSchema: "portal",
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_locations_location_id",
                schema: "portal",
                table: "report_locations",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_created_at",
                schema: "portal",
                table: "reports",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_reports_parent_report_id",
                schema: "portal",
                table: "reports",
                column: "parent_report_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_locations",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "report_recipients",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "reports",
                schema: "portal");
        }
    }
}
