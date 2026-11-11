using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Portal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LogbookReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "follow_up_note",
                schema: "portal",
                table: "logbook_reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "followed_up_at",
                schema: "portal",
                table: "logbook_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "followed_up_by",
                schema: "portal",
                table: "logbook_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "logbook_comments",
                schema: "portal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logbook_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_logbook_comments_logbook_reports_report_id",
                        column: x => x.report_id,
                        principalSchema: "portal",
                        principalTable: "logbook_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_logbook_comments_report_id",
                schema: "portal",
                table: "logbook_comments",
                column: "report_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logbook_comments",
                schema: "portal");

            migrationBuilder.DropColumn(
                name: "follow_up_note",
                schema: "portal",
                table: "logbook_reports");

            migrationBuilder.DropColumn(
                name: "followed_up_at",
                schema: "portal",
                table: "logbook_reports");

            migrationBuilder.DropColumn(
                name: "followed_up_by",
                schema: "portal",
                table: "logbook_reports");
        }
    }
}
