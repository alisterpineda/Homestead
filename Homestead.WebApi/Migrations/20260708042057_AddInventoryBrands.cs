using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddColumn<Guid>(
                name: "BrandId",
                schema: "inventory",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "inventory",
                table: "items",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "brands",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brands", x => x.Id);
                });

            // Case-insensitive uniqueness on the brand name — the canonicalisation backstop for
            // find-or-create. A functional index EF can't scaffold, so it's raw SQL (see
            // AddInventoryItemTrigramIndexes for the idiom).
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_brands_Name_lower"" ON inventory.brands (lower(""Name""));");

            migrationBuilder.CreateIndex(
                name: "IX_items_BrandId",
                schema: "inventory",
                table: "items",
                column: "BrandId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_items_brands_BrandId",
                schema: "inventory",
                table: "items",
                column: "BrandId",
                principalSchema: "inventory",
                principalTable: "brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_items_brands_BrandId",
                schema: "inventory",
                table: "items");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS inventory.""IX_brands_Name_lower"";");

            migrationBuilder.DropTable(
                name: "brands",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_items_BrandId",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "BrandId",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL)");
        }
    }
}
