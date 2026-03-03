using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsAndDocumentScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "documents",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "groups",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_groups_app_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                schema: "auth",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessLevel = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_members", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_group_members_app_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_members_groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "auth",
                        principalTable: "groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_GroupId",
                schema: "documents",
                table: "documents",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_group_members_UserId",
                schema: "auth",
                table: "group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_CreatedByUserId",
                schema: "auth",
                table: "groups",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_groups_Name",
                schema: "auth",
                table: "groups",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_groups_GroupId",
                schema: "documents",
                table: "documents",
                column: "GroupId",
                principalSchema: "auth",
                principalTable: "groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_documents_groups_GroupId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropTable(
                name: "group_members",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "auth");

            migrationBuilder.DropIndex(
                name: "IX_documents_GroupId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "documents",
                table: "documents");
        }
    }
}
