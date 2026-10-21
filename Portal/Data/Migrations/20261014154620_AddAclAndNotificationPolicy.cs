using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAclAndNotificationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acl_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    privileges = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acl_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acl_user_role",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acl_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acl_user_role", x => new { x.user_id, x.acl_role_id });
                    table.ForeignKey(
                        name: "fk_acl_user_role_acl_role_acl_role_id",
                        column: x => x.acl_role_id,
                        principalTable: "acl_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acl_user_role_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acl_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    effect = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    conditions = table.Column<string>(type: "json", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_policy", x => x.id);
                    table.CheckConstraint("ck_notification_policy_subject", "(user_id IS NULL) <> (acl_role_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_notification_policy_acl_role_acl_role_id",
                        column: x => x.acl_role_id,
                        principalTable: "acl_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_policy_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acl_user_role_acl_role_id",
                table: "acl_user_role",
                column: "acl_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_policy_acl_role_id_resource",
                table: "notification_policy",
                columns: new[] { "acl_role_id", "resource" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_policy_user_id_resource",
                table: "notification_policy",
                columns: new[] { "user_id", "resource" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acl_user_role");

            migrationBuilder.DropTable(
                name: "notification_policy");

            migrationBuilder.DropTable(
                name: "acl_role");
        }
    }
}
