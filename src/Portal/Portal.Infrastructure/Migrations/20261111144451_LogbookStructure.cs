using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogbookStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logbook_districts",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_districts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logbook_store_assignments",
                schema: "portal",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_store_assignments", x => x.store_id);
                });

            migrationBuilder.CreateTable(
                name: "logbook_district_leaders",
                schema: "portal",
                columns: table => new
                {
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_district_leaders", x => new { x.district_id, x.employee_id });
                    table.ForeignKey(
                        name: "fk_logbook_district_leaders_logbook_districts_district_id",
                        column: x => x.district_id,
                        principalSchema: "portal",
                        principalTable: "logbook_districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "logbook_regions",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_regions", x => x.id);
                    table.ForeignKey(
                        name: "fk_logbook_regions_logbook_districts_district_id",
                        column: x => x.district_id,
                        principalSchema: "portal",
                        principalTable: "logbook_districts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "logbook_region_leaders",
                schema: "portal",
                columns: table => new
                {
                    region_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_region_leaders", x => new { x.region_id, x.employee_id });
                    table.ForeignKey(
                        name: "fk_logbook_region_leaders_logbook_regions_region_id",
                        column: x => x.region_id,
                        principalSchema: "portal",
                        principalTable: "logbook_regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_district_leaders_employee_id",
                schema: "portal",
                table: "logbook_district_leaders",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_logbook_region_leaders_employee_id",
                schema: "portal",
                table: "logbook_region_leaders",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_logbook_regions_district_id",
                schema: "portal",
                table: "logbook_regions",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_logbook_store_assignments_district_id",
                schema: "portal",
                table: "logbook_store_assignments",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_logbook_store_assignments_region_id",
                schema: "portal",
                table: "logbook_store_assignments",
                column: "region_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logbook_district_leaders",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "logbook_region_leaders",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "logbook_store_assignments",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "logbook_regions",
                schema: "portal");

            migrationBuilder.DropTable(
                name: "logbook_districts",
                schema: "portal");
        }
    }
}
