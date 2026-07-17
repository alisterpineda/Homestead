using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPayees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayeeId",
                schema: "finances",
                table: "account_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payees",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DefaultCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payees_accounts_DefaultCategoryId",
                        column: x => x.DefaultCategoryId,
                        principalSchema: "finances",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payees_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payees_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Case-insensitive uniqueness on the payee name — a functional index EF can't scaffold, so it's
            // raw SQL (see AddInventoryBrands for the idiom).
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_payees_Name_lower"" ON finances.payees (lower(""Name""));");

            migrationBuilder.CreateIndex(
                name: "IX_account_transactions_PayeeId",
                schema: "finances",
                table: "account_transactions",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_payees_CreatedByUserId",
                schema: "finances",
                table: "payees",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payees_DefaultCategoryId",
                schema: "finances",
                table: "payees",
                column: "DefaultCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_payees_UpdatedByUserId",
                schema: "finances",
                table: "payees",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_account_transactions_payees_PayeeId",
                schema: "finances",
                table: "account_transactions",
                column: "PayeeId",
                principalSchema: "finances",
                principalTable: "payees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_transactions_payees_PayeeId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS finances.""IX_payees_Name_lower"";");

            migrationBuilder.DropTable(
                name: "payees",
                schema: "finances");

            migrationBuilder.DropIndex(
                name: "IX_account_transactions_PayeeId",
                schema: "finances",
                table: "account_transactions");

            migrationBuilder.DropColumn(
                name: "PayeeId",
                schema: "finances",
                table: "account_transactions");
        }
    }
}
