using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create document_revisions table
            migrationBuilder.CreateTable(
                name: "document_revisions",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_revisions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_DocumentId",
                schema: "documents",
                table: "document_revisions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_DocumentId_RevisionNumber",
                schema: "documents",
                table: "document_revisions",
                columns: new[] { "DocumentId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_revisions_FileHash",
                schema: "documents",
                table: "document_revisions",
                column: "FileHash",
                unique: true);

            // 2. Migrate existing documents' file data to revisions
            migrationBuilder.Sql("""
                INSERT INTO documents.document_revisions (
                    "Id", "RevisionNumber", "Label", "ContentType", "FileSize",
                    "FilePath", "FileHash", "FileExtension", "ExtractedText",
                    "CreatedAt", "DocumentId"
                )
                SELECT gen_random_uuid(), 1, NULL, "ContentType", "FileSize",
                    "FilePath", "FileHash", "FileExtension", "ExtractedText",
                    "CreatedAt", "Id"
                FROM documents.documents;
                """);

            // 3. Drop file columns from documents table
            migrationBuilder.DropIndex(
                name: "IX_documents_FileHash",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ContentType",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FileExtension",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FileHash",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FilePath",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FileSize",
                schema: "documents",
                table: "documents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileExtension",
                schema: "documents",
                table: "documents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                schema: "documents",
                table: "documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                schema: "documents",
                table: "documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Migrate data back from revisions to documents (latest revision per document)
            migrationBuilder.Sql("""
                UPDATE documents.documents d
                SET "ContentType" = r."ContentType",
                    "ExtractedText" = r."ExtractedText",
                    "FileExtension" = r."FileExtension",
                    "FileHash" = r."FileHash",
                    "FilePath" = r."FilePath",
                    "FileSize" = r."FileSize"
                FROM (
                    SELECT DISTINCT ON ("DocumentId") *
                    FROM documents.document_revisions
                    ORDER BY "DocumentId", "RevisionNumber" DESC
                ) r
                WHERE d."Id" = r."DocumentId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_documents_FileHash",
                schema: "documents",
                table: "documents",
                column: "FileHash",
                unique: true);

            migrationBuilder.DropTable(
                name: "document_revisions",
                schema: "documents");
        }
    }
}
