using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeBackupSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIRST, before anything else: the state table is re-keyed from a config string to a NOT NULL
            // FK into a table that does not exist yet, so every existing row is unmappable by
            // construction. The feature has no production usage — dev mirrors simply re-copy on the next
            // run — so the sanctioned answer is to wipe the bookkeeping rather than invent source rows.
            // Must precede the AddColumn below, whose NOT NULL would otherwise need a placeholder value
            // that the new foreign key immediately rejects.
            //
            // ONE-TIME OPERATOR NOTE for anyone who did run the pre-release build: this wipe plus the new
            // <root>/Documents/ prefix strands whatever is already on the mirror. Deletions are found
            // only by anti-join against these rows, so the re-created pairing plans zero deletes and
            // re-puts the whole library at the new paths — leaving the old un-prefixed year folders
            // beside it as a second full copy that no future run can ever see or prune (the target is
            // one-way; nothing enumerates it). Delete the old <root>/20NN/ and <root>/_undated/ folders
            // by hand after the first run of the new layout.
            migrationBuilder.Sql(@"DELETE FROM backup.document_backup_state;");

            migrationBuilder.DropIndex(
                name: "IX_document_backup_state_TargetKey",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropIndex(
                name: "IX_document_backup_state_TargetKey_DocumentId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropIndex(
                name: "IX_document_backup_state_TargetKey_DocumentVersionId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropColumn(
                name: "PruneBlockedAt",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "PruneBlockedCount",
                schema: "backup",
                table: "target_state");

            migrationBuilder.DropColumn(
                name: "TargetKey",
                schema: "backup",
                table: "document_backup_state");

            // No default value: the table was emptied above, and a column default here would let a future
            // insert that forgets the source id land a zero guid — which the foreign key would reject
            // anyway, just later and with a worse error.
            migrationBuilder.AddColumn<Guid>(
                name: "BackupSourceId",
                schema: "backup",
                table: "document_backup_state",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "sources",
                schema: "backup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PruneBlockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PruneBlockedCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sources_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "processing",
                        principalTable: "schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_backup_state_BackupSourceId_DocumentId",
                schema: "backup",
                table: "document_backup_state",
                columns: new[] { "BackupSourceId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_backup_state_BackupSourceId_DocumentVersionId",
                schema: "backup",
                table: "document_backup_state",
                columns: new[] { "BackupSourceId", "DocumentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sources_ScheduleId",
                schema: "backup",
                table: "sources",
                column: "ScheduleId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_document_backup_state_sources_BackupSourceId",
                schema: "backup",
                table: "document_backup_state",
                column: "BackupSourceId",
                principalSchema: "backup",
                principalTable: "sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // "One pairing per kind per target", with target keys compared the way config keys are:
            // case-insensitively. Raw SQL because an expression index cannot be expressed in the fluent
            // API — which also means it is INVISIBLE to the model snapshot, so no future migration will
            // manage it. See BackupSourceConfiguration's comment; this is load-bearing for mirror
            // correctness, not tidiness (two same-kind rows on one target prune each other's files).
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_backup_sources_SourceKind_TargetKey_Lower""
                ON backup.sources (""SourceKind"", lower(""TargetKey""));");

            // The entity type is renamed; the JOB type is not. Existing job rows keep their history and
            // stay joinable to the admin page.
            migrationBuilder.Sql(@"
                UPDATE processing.jobs
                SET ""EntityType"" = 'BackupSource'
                WHERE ""EntityType"" = 'BackupTarget';");

            // Backup schedules carried the target key inside their jsonb payload; the new payload names a
            // source row that does not exist yet. A surviving schedule would wake, find no source, and
            // skip quietly forever — so drop them and let the admin re-create the schedule alongside the
            // pairing.
            migrationBuilder.Sql(@"
                DELETE FROM processing.schedules
                WHERE ""JobType"" = 'BackupToTarget';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE processing.jobs
                SET ""EntityType"" = 'BackupTarget'
                WHERE ""EntityType"" = 'BackupSource';");

            // Dropped explicitly: the snapshot never knew about it, so DropTable below would take it with
            // the table, but saying so keeps the pair legible.
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS backup.""IX_backup_sources_SourceKind_TargetKey_Lower"";");

            // The state rows are re-keyed to a column that is about to come back empty; nothing here can
            // reconstruct which target each row belonged to, so the down path wipes them too.
            migrationBuilder.Sql(@"DELETE FROM backup.document_backup_state;");

            migrationBuilder.DropForeignKey(
                name: "FK_document_backup_state_sources_BackupSourceId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropTable(
                name: "sources",
                schema: "backup");

            migrationBuilder.DropIndex(
                name: "IX_document_backup_state_BackupSourceId_DocumentId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropIndex(
                name: "IX_document_backup_state_BackupSourceId_DocumentVersionId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.DropColumn(
                name: "BackupSourceId",
                schema: "backup",
                table: "document_backup_state");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PruneBlockedAt",
                schema: "backup",
                table: "target_state",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PruneBlockedCount",
                schema: "backup",
                table: "target_state",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetKey",
                schema: "backup",
                table: "document_backup_state",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

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
    }
}
