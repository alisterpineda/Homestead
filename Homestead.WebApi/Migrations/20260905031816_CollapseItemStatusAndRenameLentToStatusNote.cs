using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <summary>
    /// Collapses ItemStatus from six values to Active / Lent / Archived and generalises the durable-only
    /// LentTo column into a StatusNote carried by both kinds. Sold and Discarded both become Archived
    /// with the old status kept as the note ('Sold' / 'Discarded'), and InRepair — a data-less second
    /// Lent — becomes Active.
    /// <para>
    /// Migrations auto-run on startup (see Homestead.WebApi/CLAUDE.md), so every statement here must be
    /// safe on a populated database: the column is renamed in place rather than dropped and re-added,
    /// and the constraints that reference it are dropped before the data moves and re-added after.
    /// </para>
    /// </summary>
    public partial class CollapseItemStatusAndRenameLentToStatusNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the constraints that reference LentTo / the old status values first, so the rename
            // and the backfill below can't trip them.
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_lent_to_requires_lent",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            // Rename in place — the borrower notes on currently-Lent rows are kept verbatim.
            migrationBuilder.RenameColumn(
                name: "LentTo",
                schema: "inventory",
                table: "items",
                newName: "StatusNote");

            // Sold/Discarded → Archived, keeping the retired status as the note so the distinction
            // survives as free text. Those rows had LentTo NULL under the old check constraint, so
            // overwriting the note is safe. InRepair → Active (it carried no data of its own).
            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"StatusNote\" = \"Status\", \"Status\" = 'Archived' "
                + "WHERE \"Status\" IN ('Sold', 'Discarded');");

            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"Status\" = 'Active' WHERE \"Status\" = 'InRepair';");

            // Re-add without the "LentTo" IS NULL clause: a fungible row may now carry an archive note.
            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"IsContainer\" IS NULL AND \"AcquisitionKind\" IS NULL AND \"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL AND \"AcquiredFrom\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_status_note_requires_status",
                schema: "inventory",
                table: "items",
                sql: "\"StatusNote\" IS NULL OR \"Status\" IN ('Lent', 'Archived')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_items_status_note_requires_status",
                schema: "inventory",
                table: "items");

            migrationBuilder.RenameColumn(
                name: "StatusNote",
                schema: "inventory",
                table: "items",
                newName: "LentTo");

            // This downgrade is LOSSY, in two ways. Archive reasons ("Sold to Sam", and the 'Sold' /
            // 'Discarded' markers Up() wrote) are discarded here, because the old constraint only
            // tolerated a note on a Lent row — a clear is the only way the constraint can be re-added
            // without throwing on a populated database. And the collapse itself is not reversed: rows
            // that were Sold, Discarded or InRepair stay Archived / Active, since nothing records which
            // of them they used to be once the notes are gone.
            migrationBuilder.Sql(
                "UPDATE inventory.items SET \"LentTo\" = NULL WHERE \"Status\" <> 'Lent';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_lent_to_requires_lent",
                schema: "inventory",
                table: "items",
                sql: "\"LentTo\" IS NULL OR \"Status\" = 'Lent'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_items_nonfungible_fields",
                schema: "inventory",
                table: "items",
                sql: "\"Fungibility\" = 'NonFungible' OR (\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL AND \"Model\" IS NULL AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"IsContainer\" IS NULL AND \"AcquisitionKind\" IS NULL AND \"AcquisitionCost\" IS NULL AND \"AcquisitionCostCurrencyId\" IS NULL AND \"AcquiredFrom\" IS NULL)");
        }
    }
}
