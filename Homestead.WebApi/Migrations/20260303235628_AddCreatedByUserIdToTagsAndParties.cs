using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByUserIdToTagsAndParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "documents",
                table: "tags",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "documents",
                table: "parties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_CreatedByUserId",
                schema: "documents",
                table: "tags",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_parties_CreatedByUserId",
                schema: "documents",
                table: "parties",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_parties_app_users_CreatedByUserId",
                schema: "documents",
                table: "parties",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_app_users_CreatedByUserId",
                schema: "documents",
                table: "tags",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_parties_app_users_CreatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_app_users_CreatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_CreatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_parties_CreatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "documents",
                table: "parties");
        }
    }
}
