using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalShapeDiscriminator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalShape",
                schema: "finances",
                table: "account_transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Backfill: every pre-existing normal transaction is a Transfer (the only normal shape
            // that existed before this migration), so the discriminator-based mapping keeps reading
            // them as transfers. Non-normal rows (OB/BE) stay NULL.
            migrationBuilder.Sql(
                "UPDATE finances.account_transactions SET \"NormalShape\" = 'Transfer' WHERE \"Type\" = 'Normal';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalShape",
                schema: "finances",
                table: "account_transactions");
        }
    }
}
