using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tags",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    EffectiveColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags1", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_tags_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "inventory",
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tags_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tags_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "item_tags",
                schema: "inventory",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_tags", x => new { x.ItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_item_tags_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_tags_tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "inventory",
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_tags_ItemId",
                schema: "inventory",
                table: "item_tags",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_item_tags_TagId",
                schema: "inventory",
                table: "item_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_CreatedByUserId1",
                schema: "inventory",
                table: "tags",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_ParentId1",
                schema: "inventory",
                table: "tags",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_tags_Path1",
                schema: "inventory",
                table: "tags",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tags_UpdatedByUserId1",
                schema: "inventory",
                table: "tags",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_tags",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "inventory");
        }
    }
}
