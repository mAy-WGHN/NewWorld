using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Rollen_Rechte.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                name: "app_privilege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    privilege = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    area_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    area_label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    scopable = table.Column<bool>(type: "boolean", nullable: false),
                    defines_reach = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_privilege", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_privilege",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
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
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    store_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false)
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
                    refresh_token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    valid = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    family_valid = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true)
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
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
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
                    created_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: false),
                    username = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    bc_pin_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    home_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false)
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
                    scoped = table.Column<bool>(type: "boolean", nullable: false),
                    home_scope = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true),
                    valid_to = table.Column<DateTime>(type: "timestamp(0) without time zone", nullable: true),
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
                name: "ix_app_privilege_app_key",
                table: "app_privilege",
                column: "app_key");

            migrationBuilder.CreateIndex(
                name: "uniq_app_privilege_privilege",
                table: "app_privilege",
                column: "privilege",
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acl_user_role_scope");

            migrationBuilder.DropTable(
                name: "app_privilege");

            migrationBuilder.DropTable(
                name: "custom_privilege");

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
    }
}
