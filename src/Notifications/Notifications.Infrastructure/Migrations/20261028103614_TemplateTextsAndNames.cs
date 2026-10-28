using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplateTextsAndNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_names",
                schema: "notifications",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    exit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_names", x => x.employee_id);
                });

            migrationBuilder.CreateTable(
                name: "template_overrides",
                schema: "notifications",
                columns: table => new
                {
                    template_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    in_app_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    in_app_body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    push_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    push_body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    email_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email_body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_overrides", x => x.template_key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_names",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "template_overrides",
                schema: "notifications");
        }
    }
}
