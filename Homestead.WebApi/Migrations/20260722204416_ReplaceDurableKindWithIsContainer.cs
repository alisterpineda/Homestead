using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceDurableKindWithIsContainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the constraints that reference DurableKind first so the column can be dropped and
            // the new IsContainer clauses can replace them once the data is backfilled.
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            // Add the new column nullable, then backfill from the old DurableKind (persistent dev DB —
            // backfill preserves data): fungible rows stay null, container durables become TRUE, and
            // BOTH Plain and Assembly collapse to FALSE (they never differed in behaviour — a
            // non-container's assembly-ness is now derived from having component children).
            migrationBuilder.AddColumn<bool>(
                name: "IsContainer",
                schema: "inventory",
                table: "items",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"IsContainer\" = CASE "
                + "WHEN \"Fungibility\" = 'Fungible' THEN NULL "
                + "WHEN \"DurableKind\" = 'Container' THEN TRUE "
                + "ELSE FALSE END");

            migrationBuilder.DropColumn(
                name: "DurableKind",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'Fungible' OR (\"Unit\" IS NULL AND \"RestockThreshold\" IS NULL AND \"IsContainer\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"IsContainer\" IS NULL AND \"AcquisitionKind\" IS NULL AND \"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL AND \"AcquiredFrom\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            // Add DurableKind back nullable and reconstruct it from IsContainer: fungible rows stay
            // NULL, containers become 'Container', everything else 'Plain'. This downgrade is LOSSY —
            // the Assembly distinction is unrecoverable, because IsContainer never recorded it (an
            // assembly is now just a non-container that happens to have component children).
            migrationBuilder.AddColumn<string>(
                name: "DurableKind",
                schema: "inventory",
                table: "items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"DurableKind\" = CASE "
                + "WHEN \"Fungibility\" = 'Fungible' THEN NULL "
                + "WHEN \"IsContainer\" THEN 'Container' "
                + "ELSE 'Plain' END");

            migrationBuilder.DropColumn(
                name: "IsContainer",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'Fungible' OR (\"Unit\" IS NULL AND \"RestockThreshold\" IS NULL AND \"DurableKind\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL AND \"AcquisitionKind\" IS NULL AND \"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL AND \"AcquiredFrom\" IS NULL)");
        }
    }
}
