using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAiClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Summary",
                schema: "documents",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_providers",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Configuration = table.Column<string>(type: "jsonb", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_providers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ai_providers_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_suggestions",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SuggestedValue = table.Column<string>(type: "text", nullable: false),
                    OriginalValue = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AiProviderId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_suggestions_ai_providers_AiProviderId",
                        column: x => x.AiProviderId,
                        principalSchema: "settings",
                        principalTable: "ai_providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_document_suggestions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_providers_CreatedByUserId",
                schema: "settings",
                table: "ai_providers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_providers_Name",
                schema: "settings",
                table: "ai_providers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_suggestions_AiProviderId",
                schema: "documents",
                table: "document_suggestions",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_document_suggestions_DocumentId_Field",
                schema: "documents",
                table: "document_suggestions",
                columns: new[] { "DocumentId", "Field" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_suggestions",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "ai_providers",
                schema: "settings");

            migrationBuilder.DropColumn(
                name: "Summary",
                schema: "documents",
                table: "documents");
        }
    }
}
