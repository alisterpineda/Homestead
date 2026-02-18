using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagsAndDocumentTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tags");

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_tags_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "tags",
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_tags",
                schema: "documents",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_tags", x => new { x.DocumentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_document_tags_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_tags_tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "tags",
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_tags_DocumentId",
                schema: "documents",
                table: "document_tags",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_tags_TagId",
                schema: "documents",
                table: "document_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_ParentId",
                schema: "tags",
                table: "tags",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Path",
                schema: "tags",
                table: "tags",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_tags",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "tags");
        }
    }
}
