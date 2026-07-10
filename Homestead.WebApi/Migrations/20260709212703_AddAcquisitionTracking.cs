using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "inventory",
                table: "stock_movements",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcquiredFrom",
                schema: "inventory",
                table: "items",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AcquisitionCost",
                schema: "inventory",
                table: "items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcquisitionKind",
                schema: "inventory",
                table: "items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items",
                column: "AcquisitionCostCurrencyId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_acquisition_cost_currency_pair",
                schema: "inventory",
                table: "items",
                sql: "(\"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL) OR (\"AcquisitionCost\" IS NOT NULL AND \"AcquisitionCostCurrencyId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL AND \"AcquisitionKind\" IS NULL AND \"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL AND \"AcquiredFrom\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_items_commodities_AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items",
                column: "AcquisitionCostCurrencyId",
                principalSchema: "finances",
                principalTable: "commodities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_items_commodities_AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropIndex(
                name: "IX_items_AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_acquisition_cost_currency_pair",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "AcquiredFrom",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "AcquisitionCost",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "AcquisitionCostCurrencyId",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropColumn(
                name: "AcquisitionKind",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL)");
        }
    }
}
