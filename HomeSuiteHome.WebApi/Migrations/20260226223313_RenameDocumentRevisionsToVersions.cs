using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameDocumentRevisionsToVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename table
            migrationBuilder.RenameTable(
                name: "document_revisions",
                schema: "documents",
                newName: "document_versions",
                newSchema: "documents");

            // Rename column
            migrationBuilder.RenameColumn(
                name: "RevisionNumber",
                schema: "documents",
                table: "document_versions",
                newName: "VersionNumber");

            // Rename indexes
            migrationBuilder.RenameIndex(
                name: "IX_document_revisions_DocumentId",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_versions_DocumentId");

            migrationBuilder.RenameIndex(
                name: "IX_document_revisions_FileHash",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_versions_FileHash");

            migrationBuilder.RenameIndex(
                name: "IX_document_revisions_DocumentId_RevisionNumber",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_versions_DocumentId_VersionNumber");

            // Rename PK and FK constraints
            migrationBuilder.Sql(
                """ALTER TABLE documents.document_versions RENAME CONSTRAINT "PK_document_revisions" TO "PK_document_versions";""");

            migrationBuilder.Sql(
                """ALTER TABLE documents.document_versions RENAME CONSTRAINT "FK_document_revisions_documents_DocumentId" TO "FK_document_versions_documents_DocumentId";""");

            // Update processing job type values
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'UploadDocumentVersion' WHERE "Type" = 'UploadDocumentRevision';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert processing job type values
            migrationBuilder.Sql(
                """UPDATE processing.jobs SET "Type" = 'UploadDocumentRevision' WHERE "Type" = 'UploadDocumentVersion';""");

            // Revert FK and PK constraints
            migrationBuilder.Sql(
                """ALTER TABLE documents.document_versions RENAME CONSTRAINT "FK_document_versions_documents_DocumentId" TO "FK_document_revisions_documents_DocumentId";""");

            migrationBuilder.Sql(
                """ALTER TABLE documents.document_versions RENAME CONSTRAINT "PK_document_versions" TO "PK_document_revisions";""");

            // Revert indexes
            migrationBuilder.RenameIndex(
                name: "IX_document_versions_DocumentId_VersionNumber",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_revisions_DocumentId_RevisionNumber");

            migrationBuilder.RenameIndex(
                name: "IX_document_versions_FileHash",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_revisions_FileHash");

            migrationBuilder.RenameIndex(
                name: "IX_document_versions_DocumentId",
                schema: "documents",
                table: "document_versions",
                newName: "IX_document_revisions_DocumentId");

            // Revert column
            migrationBuilder.RenameColumn(
                name: "VersionNumber",
                schema: "documents",
                table: "document_versions",
                newName: "RevisionNumber");

            // Revert table
            migrationBuilder.RenameTable(
                name: "document_versions",
                schema: "documents",
                newName: "document_revisions",
                newSchema: "documents");
        }
    }
}
