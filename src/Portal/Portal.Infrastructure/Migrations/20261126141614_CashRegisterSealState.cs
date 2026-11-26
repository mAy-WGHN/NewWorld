using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashRegisterSealState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_sealed",
                schema: "portal",
                table: "cash_registers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "state_changed_at",
                schema: "portal",
                table: "cash_registers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "state_changed_by",
                schema: "portal",
                table: "cash_registers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // keep what is there: a register with a seal is sealed, the state was last set when it was sealed, otherwise when it was last recorded
            migrationBuilder.Sql(
                """
                UPDATE portal.cash_registers
                SET is_sealed = sealed_at IS NOT NULL,
                    state_changed_at = COALESCE(sealed_at, last_recorded_at),
                    state_changed_by = COALESCE(sealed_by, last_recorded_by);
                """);

            migrationBuilder.DropColumn(
                name: "sealed_at",
                schema: "portal",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "sealed_by",
                schema: "portal",
                table: "cash_registers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sealed_at",
                schema: "portal",
                table: "cash_registers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sealed_by",
                schema: "portal",
                table: "cash_registers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE portal.cash_registers
                SET sealed_at = CASE WHEN is_sealed THEN state_changed_at END,
                    sealed_by = CASE WHEN is_sealed THEN state_changed_by END;
                """);

            migrationBuilder.DropColumn(
                name: "is_sealed",
                schema: "portal",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "state_changed_at",
                schema: "portal",
                table: "cash_registers");

            migrationBuilder.DropColumn(
                name: "state_changed_by",
                schema: "portal",
                table: "cash_registers");
        }
    }
}
