using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PermissionCacheWithPrivileges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employee_ids_json",
                schema: "workforce",
                table: "permission_snapshots");

            migrationBuilder.DropColumn(
                name: "external_id",
                schema: "workforce",
                table: "permission_snapshots");

            migrationBuilder.RenameColumn(
                name: "store_ids_json",
                schema: "workforce",
                table: "permission_snapshots",
                newName: "privileges_json");

            // Old rows held store lists in another format; a later PermissionsChanged (higher version) refills them.
            migrationBuilder.Sql("UPDATE workforce.permission_snapshots SET privileges_json = '{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "privileges_json",
                schema: "workforce",
                table: "permission_snapshots",
                newName: "store_ids_json");

            migrationBuilder.AddColumn<string>(
                name: "employee_ids_json",
                schema: "workforce",
                table: "permission_snapshots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                schema: "workforce",
                table: "permission_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
