using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class UnifyTransactionDescriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Copy non-blank balance-entry notes into description before dropping the column.
            migrationBuilder.Sql("""
                UPDATE finances.account_transactions
                SET "Description" = TRIM("Note")
                WHERE "Type" = 'BalanceEntry'
                  AND "Note" IS NOT NULL
                  AND TRIM("Note") <> '';
                """);

            // Re-tag existing opening-balance rows with the new TPH discriminator value.
            // Keyed on the current-world definition (Normal type + literal description).
            migrationBuilder.Sql("""
                UPDATE finances.account_transactions
                SET "Type" = 'OpeningBalance'
                WHERE "Type" = 'Normal' AND "Description" = 'Opening balance';
                """);

            migrationBuilder.DropColumn(
                name: "Note",
                schema: "finances",
                table: "account_transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                schema: "finances",
                table: "account_transactions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            // Reverse the discriminator flip. No reverse move for Note (data loss accepted on rollback).
            migrationBuilder.Sql("""
                UPDATE finances.account_transactions
                SET "Type" = 'Normal'
                WHERE "Type" = 'OpeningBalance';
                """);
        }
    }
}
