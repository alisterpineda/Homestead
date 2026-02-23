using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthAndDocumentOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "app_users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_CreatedByUserId",
                schema: "documents",
                table: "documents",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_OwnerUserId",
                schema: "documents",
                table: "documents",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_app_users_ExternalSubject",
                schema: "identity",
                table: "app_users",
                column: "ExternalSubject",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_CreatedByUserId",
                schema: "documents",
                table: "documents",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_OwnerUserId",
                schema: "documents",
                table: "documents",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_CreatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_OwnerUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropTable(
                name: "app_users",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_documents_CreatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_OwnerUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "documents",
                table: "documents");
        }
    }
}
