using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "backup");

            migrationBuilder.CreateTable(
                name: "document_backup_state",
                schema: "backup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemoteRelativePath = table.Column<string>(type: "text", nullable: false),
                    RemoteHandle = table.Column<string>(type: "text", nullable: true),
                    BackedUpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    BackedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_backup_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "target_state",
                schema: "backup",
                columns: table => new
                {
                    TargetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PruneBlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PruneBlockedCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_target_state", x => x.TargetKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_backup_state_TargetKey",
                schema: "backup",
                table: "document_backup_state",
                column: "TargetKey");

            migrationBuilder.CreateIndex(
                name: "IX_document_backup_state_TargetKey_DocumentId",
                schema: "backup",
                table: "document_backup_state",
                columns: new[] { "TargetKey", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_backup_state_TargetKey_DocumentVersionId",
                schema: "backup",
                table: "document_backup_state",
                columns: new[] { "TargetKey", "DocumentVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_backup_state",
                schema: "backup");

            migrationBuilder.DropTable(
                name: "target_state",
                schema: "backup");
        }
    }
}
