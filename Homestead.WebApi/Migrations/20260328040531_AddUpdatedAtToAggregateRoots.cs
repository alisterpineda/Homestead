using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtToAggregateRoots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "tags",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "tags",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "parties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "parties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "auth",
                table: "groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "auth",
                table: "groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_UpdatedByUserId",
                schema: "documents",
                table: "tags",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_parties_UpdatedByUserId",
                schema: "documents",
                table: "parties",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_UpdatedByUserId",
                schema: "auth",
                table: "groups",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_UpdatedAt",
                schema: "documents",
                table: "documents",
                column: "UpdatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_documents_UpdatedByUserId",
                schema: "documents",
                table: "documents",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_UpdatedByUserId",
                schema: "documents",
                table: "documents",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_app_users_UpdatedByUserId",
                schema: "auth",
                table: "groups",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_parties_app_users_UpdatedByUserId",
                schema: "documents",
                table: "parties",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_app_users_UpdatedByUserId",
                schema: "documents",
                table: "tags",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_UpdatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_app_users_UpdatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_parties_app_users_UpdatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_app_users_UpdatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_tags_UpdatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "IX_parties_UpdatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropIndex(
                name: "IX_groups_UpdatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "IX_documents_UpdatedAt",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_documents_UpdatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "documents",
                table: "documents");
        }
    }
}
