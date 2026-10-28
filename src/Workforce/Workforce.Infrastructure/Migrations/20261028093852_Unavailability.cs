using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Unavailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "schedule_adjustment_handler_ids",
                schema: "workforce",
                table: "approver_copies",
                type: "uuid[]",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "unavailability_entries",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_absence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unavailability_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unavailability_kinds",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paid_time = table.Column<bool>(type: "boolean", nullable: false),
                    schedule_symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    work_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unavailability_kinds", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "workforce",
                table: "unavailability_kinds",
                columns: new[] { "id", "code", "description", "is_active", "is_system", "paid_time", "schedule_symbol", "work_code" },
                values: new object[,]
                {
                    { new Guid("0cc15a10-6a9e-114f-323c-07b05a51d0fd"), "UNENTSCHULDIGT", "Unentschuldigt gefehlt", true, false, false, "UE", null },
                    { new Guid("133f5fe9-25e4-8e76-9f43-8a5386427cff"), "URLAUB", "Urlaub", true, true, true, "U", null },
                    { new Guid("2156e40d-a9a9-c606-9343-c09312fd2c7a"), "PRÜFUNGSVORBEREITUNG", "PRÜFUNGSVORBEREITUNG", true, false, true, "PV", null },
                    { new Guid("244f5502-e60c-cbbd-7983-4f470ccb888b"), "SONDERURLAUB", "Sonder-Urlaub", true, false, false, "SU", null },
                    { new Guid("2e76b6ee-4f1b-9d17-f66e-ba864f7769af"), "PFLEGE ANGEHÖRIGE", "Pflege od. Betreuung von Ang.", true, false, false, "PA", null },
                    { new Guid("410e9df8-5347-69cc-6062-2373c8fe2b5b"), "ROTATIONSFREI", "Feiertag Rotation", true, false, false, "RF", null },
                    { new Guid("434a8137-970b-a4c7-d000-db2d06cbb4e0"), "KLASSENFAHRT", "unbezahlt Info Klassenfahrt", true, false, false, null, null },
                    { new Guid("4fe6d2cd-eed6-96b3-0833-ca447ab437ef"), "REHA", "Reha-Maßnahme", true, false, false, "Reh", null },
                    { new Guid("52880e1e-bd34-567d-cd0e-21fef328cf57"), "URLAUBSANTRAGSZEITRAUM", "Urlaubsantrags Zeitraum", true, false, false, "UZ", null },
                    { new Guid("5fec69e0-6d51-a835-4263-01a9e142c06a"), "UNBEZ. U", "Unbezahlter Urlaub", true, false, false, "UBE", null },
                    { new Guid("69159e9c-ad43-2b25-959b-39685c7ad2dc"), "BESCHÄFTIGUNGSVERBOT", "Beschäftigungsverbot", true, false, false, "BV", null },
                    { new Guid("6d060a5e-d50a-9bb9-129c-fb1af1497bb7"), "FORTBILDUNG", "Fortbildung", true, false, false, "FB", null },
                    { new Guid("6db1b26f-3c23-7402-1e60-16740c2316a6"), "KRANK", "Krank", true, true, true, "K", null },
                    { new Guid("7a9002c3-7f0b-16ad-61c4-1dcea4162d43"), "QUARANTÄNE", "Quarantäne", true, false, false, "Q", null },
                    { new Guid("8215f40e-1589-ea9d-3a9d-fd5d43aa8c51"), "FREI", "Freier Tag", true, false, false, "FT", null },
                    { new Guid("896ccbbb-9f9e-9544-4f2b-1b35e5888bee"), "KINDKRANK", "krank Kind mit Bescheinig", true, false, true, "KK", null },
                    { new Guid("90c1a3e4-d7cc-ee95-d813-70164a8f010b"), "MEHRARBEITSABBAU", "Abbau von Mehrarbeit", true, false, false, "MA", null },
                    { new Guid("9d69add9-692b-870d-3a44-7eb11ad7a421"), "KRANK I URLAUB", "Krank im urlaub", true, true, true, "KU", null },
                    { new Guid("bc70dade-b242-a8a2-3629-cf2b04aa19c4"), "WIEDEREINGLIEDERUNG", "Wiedereingliederung", true, false, false, "WG", null },
                    { new Guid("c8b57c0f-18e3-0cbc-dc78-f6dc1703c862"), "PRÜFUNG", "Prüfung", true, false, true, "P", null },
                    { new Guid("eb3e1cb5-6e9c-1146-9319-d4a3d24a025f"), "TOD", "Tod", true, false, true, "Tod", null },
                    { new Guid("f078f3e2-9174-7bb7-0067-03ffe157b43c"), "UB", "unwiderr. bezahlt Freistellung", true, false, true, "UB", null },
                    { new Guid("f32697e5-fc86-f6d5-d54a-4f8e9a747207"), "MUTTERSCHUTZ", "Mutterschutz", true, false, false, "MS", null },
                    { new Guid("fd8d0414-c465-9c27-778e-c7545e41f9fc"), "ELTERNZEIT", "Elternzeit", true, false, false, "EZ", null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_unavailability_entries_employee_id_from",
                schema: "workforce",
                table: "unavailability_entries",
                columns: new[] { "employee_id", "from" });

            migrationBuilder.CreateIndex(
                name: "ix_unavailability_entries_source_absence_id",
                schema: "workforce",
                table: "unavailability_entries",
                column: "source_absence_id");

            migrationBuilder.CreateIndex(
                name: "ix_unavailability_kinds_code",
                schema: "workforce",
                table: "unavailability_kinds",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unavailability_entries",
                schema: "workforce");

            migrationBuilder.DropTable(
                name: "unavailability_kinds",
                schema: "workforce");

            migrationBuilder.DropColumn(
                name: "schedule_adjustment_handler_ids",
                schema: "workforce",
                table: "approver_copies");
        }
    }
}
