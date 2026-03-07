using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_DeletedAt",
                schema: "documents",
                table: "documents",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_documents_DeletedByUserId",
                schema: "documents",
                table: "documents",
                column: "DeletedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_DeletedByUserId",
                schema: "documents",
                table: "documents",
                column: "DeletedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_DeletedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_DeletedAt",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_DeletedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                schema: "documents",
                table: "documents");
        }
    }
}
