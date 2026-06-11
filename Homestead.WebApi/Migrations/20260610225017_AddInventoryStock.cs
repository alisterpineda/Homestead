using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_entries",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContainerItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_entries", x => x.Id);
                    table.CheckConstraint("CK_stock_entries_location_exactly_one", "(\"AreaId\" IS NOT NULL AND \"ContainerItemId\" IS NULL) OR (\"AreaId\" IS NULL AND \"ContainerItemId\" IS NOT NULL)");
                    table.CheckConstraint("CK_stock_entries_quantity_non_negative", "\"Quantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_stock_entries_areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "inventory",
                        principalTable: "areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_entries_items_ContainerItemId",
                        column: x => x.ContainerItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_entries_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FromAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromContainerItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToContainerItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    UnitCostCurrencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("CK_stock_movements_quantity_positive", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_stock_movements_unit_cost_currency_pair", "(\"UnitCost\" IS NULL AND \"UnitCostCurrencyId\" IS NULL) OR (\"UnitCost\" IS NOT NULL AND \"UnitCostCurrencyId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_stock_movements_areas_FromAreaId",
                        column: x => x.FromAreaId,
                        principalSchema: "inventory",
                        principalTable: "areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_movements_areas_ToAreaId",
                        column: x => x.ToAreaId,
                        principalSchema: "inventory",
                        principalTable: "areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_movements_commodities_UnitCostCurrencyId",
                        column: x => x.UnitCostCurrencyId,
                        principalSchema: "finances",
                        principalTable: "commodities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_items_FromContainerItemId",
                        column: x => x.FromContainerItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_movements_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_items_RelatedItemId",
                        column: x => x.RelatedItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_movements_items_ToContainerItemId",
                        column: x => x.ToContainerItemId,
                        principalSchema: "inventory",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_movements_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_entries_AreaId",
                schema: "inventory",
                table: "stock_entries",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_entries_ContainerItemId",
                schema: "inventory",
                table: "stock_entries",
                column: "ContainerItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_entries_ItemId",
                schema: "inventory",
                table: "stock_entries",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_entries_ItemId_AreaId_ContainerItemId",
                schema: "inventory",
                table: "stock_entries",
                columns: new[] { "ItemId", "AreaId", "ContainerItemId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_CreatedByUserId",
                schema: "inventory",
                table: "stock_movements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_FromAreaId",
                schema: "inventory",
                table: "stock_movements",
                column: "FromAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_FromContainerItemId",
                schema: "inventory",
                table: "stock_movements",
                column: "FromContainerItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId_OccurredAt",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "ItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_Kind",
                schema: "inventory",
                table: "stock_movements",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_RelatedItemId",
                schema: "inventory",
                table: "stock_movements",
                column: "RelatedItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ToAreaId",
                schema: "inventory",
                table: "stock_movements",
                column: "ToAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ToContainerItemId",
                schema: "inventory",
                table: "stock_movements",
                column: "ToContainerItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_UnitCostCurrencyId",
                schema: "inventory",
                table: "stock_movements",
                column: "UnitCostCurrencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_entries",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");
        }
    }
}
