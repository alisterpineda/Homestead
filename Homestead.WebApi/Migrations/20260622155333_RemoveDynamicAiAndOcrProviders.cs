using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDynamicAiAndOcrProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_suggestions_ai_providers_AiProviderId",
                schema: "documents",
                table: "document_suggestions");

            migrationBuilder.DropTable(
                name: "ai_providers",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "text_extraction_engines",
                schema: "settings");

            migrationBuilder.DropIndex(
                name: "IX_document_suggestions_AiProviderId",
                schema: "documents",
                table: "document_suggestions");

            migrationBuilder.DropColumn(
                name: "AiProviderId",
                schema: "documents",
                table: "document_suggestions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiProviderId",
                schema: "documents",
                table: "document_suggestions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_providers",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Configuration = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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
                name: "text_extraction_engines",
                schema: "settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Configuration = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EngineType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_extraction_engines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_text_extraction_engines_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_suggestions_AiProviderId",
                schema: "documents",
                table: "document_suggestions",
                column: "AiProviderId");

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
                name: "IX_text_extraction_engines_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_text_extraction_engines_Name",
                schema: "settings",
                table: "text_extraction_engines",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_document_suggestions_ai_providers_AiProviderId",
                schema: "documents",
                table: "document_suggestions",
                column: "AiProviderId",
                principalSchema: "settings",
                principalTable: "ai_providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
