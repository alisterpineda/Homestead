using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceIsContainerWithDurableKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the constraints that reference IsContainer first so the column can be dropped and
            // the new DurableKind clauses can replace them once the data is backfilled.
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            // Add the new column nullable, then backfill from the old IsContainer flag (persistent
            // dev DB — backfill preserves data): fungible rows stay null, container durables become
            // 'Container', everything else 'Plain'.
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
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL)");
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

            migrationBuilder.AddColumn<bool>(
                name: "IsContainer",
                schema: "inventory",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Restore the flag: only 'Container' durables were containers; assemblies and plain
            // items collapse back to false (the Assembly distinction is lost on downgrade). Scope to
            // non-fungible rows — a fungible row's DurableKind is NULL, and "NULL = 'Container'" is
            // SQL NULL, which would violate the NOT NULL column.
            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"IsContainer\" = (\"DurableKind\" = 'Container') "
                + "WHERE \"Fungibility\" = 'NonFungible'");

            migrationBuilder.DropColumn(
                name: "DurableKind",
                schema: "inventory",
                table: "items");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_fungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'Fungible' OR (\"Unit\" IS NULL AND \"RestockThreshold\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"IsContainer\" = FALSE)");
        }
    }
}
