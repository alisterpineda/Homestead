using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "areas",
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
                    table.PrimaryKey("PK_areas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fungibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsContainer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Compartment = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    ValueCurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Serial = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AcquiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LentTo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RestockThreshold = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.CheckConstraint("CK_items_fungible_fields", "\"Fungibility\" = 'Fungible' OR (\"Unit\" IS NULL AND \"RestockThreshold\" IS NULL)");
                    table.CheckConstraint("CK_items_fungible_status", "\"Fungibility\" = 'NonFungible' OR \"Status\" IN ('Active', 'Archived')");
                    table.CheckConstraint("CK_items_lent_to_requires_lent", "\"LentTo\" IS NULL OR \"Status\" = 'Lent'");
                    table.CheckConstraint("CK_items_location_not_both", "NOT (\"AreaId\" IS NOT NULL AND \"ParentItemId\" IS NOT NULL)");
                    table.CheckConstraint("CK_items_nonfungible_fields", "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"IsContainer\" = FALSE)");
                    table.CheckConstraint("CK_items_value_currency_pair", "(\"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL) OR (\"Value\" IS NOT NULL AND \"ValueCurrencyId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_items_areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "inventory",
                        principalTable: "areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_commodities_ValueCurrencyId",
                        column: x => x.ValueCurrencyId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_items_ParentItemId",
                        column: x => x.ParentItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_items_AreaId",
                schema: "inventory",
                table: "items",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_items_CreatedByUserId",
                schema: "inventory",
                table: "items",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_items_ParentItemId",
                schema: "inventory",
                table: "items",
                column: "ParentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_items_Status",
                schema: "inventory",
                table: "items",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_items_UpdatedByUserId",
                schema: "inventory",
                table: "items",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_items_ValueCurrencyId",
                schema: "inventory",
                table: "items",
                column: "ValueCurrencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "areas",
                schema: "inventory");
        }
    }
}
