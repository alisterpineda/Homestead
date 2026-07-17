using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSourceDocumentVersionWithDocumentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transaction_document_links",
                schema: "finances",
                columns: table => new
                {
                    AccountTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_document_links", x => new { x.AccountTransactionId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_transaction_document_links_account_transactions_AccountTran~",
                        column: x => x.AccountTransactionId,
                        principalSchema: "finances",
                        principalTable: "account_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transaction_document_links_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transaction_document_links_DocumentId",
                schema: "finances",
                table: "transaction_document_links",
                column: "DocumentId");

            // Backfill: translate each version-grained provenance stamp into a document-grained
            // import-origin link. The INNER JOIN resolves version → parent document and silently
            // drops stamps whose version is gone (a deleted version can be neither re-imported nor
            // rolled back). FK-safe: version rows cascade-delete with their document, so a surviving
            // version row implies a surviving document row. No dedup needed: one transaction carries
            // at most one stamp, so the composite PK (AccountTransactionId, DocumentId) cannot collide.
            migrationBuilder.Sql("""
                INSERT INTO finances.transaction_document_links
                    ("AccountTransactionId", "DocumentId", "Origin", "CreatedAt", "CreatedByUserId")
                SELECT t."Id", dv."DocumentId", 'StatementImport', t."CreatedAt", t."CreatedByUserId"
                FROM finances.account_transactions t
                JOIN documents.document_versions dv ON dv."Id" = t."SourceDocumentVersionId"
                WHERE t."SourceDocumentVersionId" IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transaction_document_links",
                schema: "finances");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_SourceDocumentVersionId",
                schema: "finances",
                table: "account_transactions",
                column: "SourceDocumentVersionId");
        }
    }
}
