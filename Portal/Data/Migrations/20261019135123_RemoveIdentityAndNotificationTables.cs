using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <summary>
    /// Benutzer, Rollen, Rechte, Scopes, Filialen, Push-Abos und Refresh-Tokens gehören jetzt den Diensten Rollen-Rechte und Benachrichtigungen. ACHTUNG Datenverlust:
    /// Für die Produktions-DB erst anwenden, wenn Benutzer, Filialen und Push-Abos übernommen sind (bis dahin nur den Fremdschlüssel fk_report_location_locations_location_id
    /// entfernen und die Tabellen stehen lassen). Neue Datenbanken (Dev/Test) legen sie mit den früheren Migrationen an und räumen sie hier wieder weg.
    /// </summary>
    /// <inheritdoc />
    public partial class RemoveIdentityAndNotificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_report_location_locations_location_id",
                table: "report_location");

            migrationBuilder.DropTable(
                name: "acl_user_role_scope");

            migrationBuilder.DropTable(
                name: "custom_privilege");

            migrationBuilder.DropTable(
                name: "notification_policy");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "scope_member");

            migrationBuilder.DropTable(
                name: "acl_user_role");

            migrationBuilder.DropTable(
                name: "scope");

            migrationBuilder.DropTable(
                name: "acl_role");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "locations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acl_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    privileges = table.Column<string>(type: "json", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acl_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_privilege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_privilege", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    store_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    family_valid = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true),
                    refresh_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    valid = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scope",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    bc_pin_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    bug_notification_min_severity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    bug_notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    home_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_weekdays = table.Column<string>(type: "json", nullable: false),
                    other_notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    suggestion_notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    theft_notifications_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    username = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_locations_home_location_id",
                        column: x => x.home_location_id,
                        principalTable: "locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scope_member",
                columns: table => new
                {
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scope_member", x => new { x.scope_id, x.dimension, x.ref_id });
                    table.ForeignKey(
                        name: "fk_scope_member_scope_scope_id",
                        column: x => x.scope_id,
                        principalTable: "scope",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "acl_user_role",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acl_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    home_scope = table.Column<bool>(type: "boolean", nullable: false),
                    scoped = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true),
                    valid_to = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true)
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
                    acl_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    conditions = table.Column<string>(type: "json", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    effect = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    @event = table.Column<string>(name: "event", type: "character varying(100)", maxLength: 100, nullable: false),
                    resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    auth_token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    public_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "acl_user_role_scope",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acl_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acl_user_role_scope", x => new { x.user_id, x.acl_role_id, x.scope_id });
                    table.ForeignKey(
                        name: "fk_acl_user_role_scope_acl_user_role_user_id_acl_role_id",
                        columns: x => new { x.user_id, x.acl_role_id },
                        principalTable: "acl_user_role",
                        principalColumns: new[] { "user_id", "acl_role_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acl_user_role_scope_scope_scope_id",
                        column: x => x.scope_id,
                        principalTable: "scope",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uniq_acl_role_name",
                table: "acl_role",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acl_user_role_acl_role_id",
                table: "acl_user_role",
                column: "acl_role_id");

            migrationBuilder.CreateIndex(
                name: "ix_acl_user_role_scope_scope_id",
                table: "acl_user_role_scope",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "uniq_custom_privilege_key",
                table: "custom_privilege",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uniq_location_store_code",
                table: "locations",
                column: "store_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_policy_acl_role_id_resource",
                table: "notification_policy",
                columns: new[] { "acl_role_id", "resource" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_policy_user_id_resource",
                table: "notification_policy",
                columns: new[] { "user_id", "resource" });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uniq_push_subscription_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_refresh_token",
                table: "refresh_tokens",
                column: "refresh_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uniq_scope_name",
                table: "scope",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scope_member_dimension_ref_id",
                table: "scope_member",
                columns: new[] { "dimension", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "ix_users_home_location_id",
                table: "users",
                column: "home_location_id");

            migrationBuilder.CreateIndex(
                name: "uniq_user_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_report_location_locations_location_id",
                table: "report_location",
                column: "location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
