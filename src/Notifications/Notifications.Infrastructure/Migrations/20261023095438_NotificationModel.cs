using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "notifications",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => new { x.notification_id, x.employee_id, x.channel });
                });

            migrationBuilder.CreateTable(
                name: "in_app_notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    deep_link = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_in_app_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notifications",
                columns: table => new
                {
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferences", x => new { x.employee_id, x.template_key, x.channel });
                });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_at",
                schema: "notifications",
                table: "deliveries",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_in_app_notifications_employee_id_created_at",
                schema: "notifications",
                table: "in_app_notifications",
                columns: new[] { "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_in_app_notifications_notification_id_employee_id",
                schema: "notifications",
                table: "in_app_notifications",
                columns: new[] { "notification_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "in_app_notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notifications");
        }
    }
}
