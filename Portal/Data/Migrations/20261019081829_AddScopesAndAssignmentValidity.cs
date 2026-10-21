using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScopesAndAssignmentValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "home_location_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "home_scope",
                table: "acl_user_role",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "scoped",
                table: "acl_user_role",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "valid_from",
                table: "acl_user_role",
                type: "timestamp(0) without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "valid_to",
                table: "acl_user_role",
                type: "timestamp(0) without time zone",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_users_home_location_id",
                table: "users",
                column: "home_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_acl_user_role_scope_scope_id",
                table: "acl_user_role_scope",
                column: "scope_id");

            migrationBuilder.CreateIndex(
                name: "uniq_scope_name",
                table: "scope",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scope_member_dimension_ref_id",
                table: "scope_member",
                columns: new[] { "dimension", "ref_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_users_locations_home_location_id",
                table: "users",
                column: "home_location_id",
                principalTable: "locations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_locations_home_location_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "acl_user_role_scope");

            migrationBuilder.DropTable(
                name: "scope_member");

            migrationBuilder.DropTable(
                name: "scope");

            migrationBuilder.DropIndex(
                name: "ix_users_home_location_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "home_location_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "home_scope",
                table: "acl_user_role");

            migrationBuilder.DropColumn(
                name: "scoped",
                table: "acl_user_role");

            migrationBuilder.DropColumn(
                name: "valid_from",
                table: "acl_user_role");

            migrationBuilder.DropColumn(
                name: "valid_to",
                table: "acl_user_role");
        }
    }
}
