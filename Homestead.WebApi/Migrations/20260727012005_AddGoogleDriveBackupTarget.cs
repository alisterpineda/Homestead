using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveBackupTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthFailedAt",
                schema: "backup",
                table: "target_state",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthFailedReason",
                schema: "backup",
                table: "target_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectedAccountEmail",
                schema: "backup",
                table: "target_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedGrant",
                schema: "backup",
                table: "target_state",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GrantIssuedAt",
                schema: "backup",
                table: "target_state",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "target_folders",
                schema: "backup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    RemoteFolderId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_target_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_target_folders_target_state_TargetKey",
                        column: x => x.TargetKey,
                        principalSchema: "backup",
                        principalTable: "target_state",
                        principalColumn: "TargetKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                schema: "backup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionUri = table.Column<string>(type: "text", nullable: false),
                    BytesCommitted = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_upload_sessions_sources_BackupSourceId",
                        column: x => x.BackupSourceId,
                        principalSchema: "backup",
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_target_folders_TargetKey_Path",
                schema: "backup",
                table: "target_folders",
                columns: new[] { "TargetKey", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_BackupSourceId_DocumentVersionId",
                schema: "backup",
                table: "upload_sessions",
                columns: new[] { "BackupSourceId", "DocumentVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "target_folders",
                schema: "backup");

            migrationBuilder.DropTable(
                name: "upload_sessions",
                schema: "backup");

            migrationBuilder.DropColumn(
                name: "AuthFailedAt",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "AuthFailedReason",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "ConnectedAccountEmail",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "EncryptedGrant",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "GrantIssuedAt",
                schema: "backup",
                table: "target_state");
        }
    }
}
