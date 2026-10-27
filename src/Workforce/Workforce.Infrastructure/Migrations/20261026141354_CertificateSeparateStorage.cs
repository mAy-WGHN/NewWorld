using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CertificateSeparateStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The bytes used to live in the database; they are not moved (nothing is productive yet): documents of a development
            // database count as deleted, the status stays. New uploads go to the separate file storage.
            migrationBuilder.Sql("UPDATE workforce.certificate_documents SET content_deleted_at = now() WHERE content_deleted_at IS NULL;");

            migrationBuilder.DropTable(
                name: "certificate_files",
                schema: "workforce");

            migrationBuilder.AddColumn<Guid>(
                name: "storage_id",
                schema: "workforce",
                table: "certificate_documents",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");
            migrationBuilder.Sql("ALTER TABLE workforce.certificate_documents ALTER COLUMN storage_id DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "certificate_settings",
                schema: "workforce",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    retention_after_end_days = table.Column<int>(type: "integer", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_certificate_documents_storage_id",
                schema: "workforce",
                table: "certificate_documents",
                column: "storage_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_settings",
                schema: "workforce");

            migrationBuilder.DropIndex(
                name: "ix_certificate_documents_storage_id",
                schema: "workforce",
                table: "certificate_documents");

            migrationBuilder.DropColumn(
                name: "storage_id",
                schema: "workforce",
                table: "certificate_documents");

            migrationBuilder.CreateTable(
                name: "certificate_files",
                schema: "workforce",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_files", x => x.document_id);
                });
        }
    }
}
