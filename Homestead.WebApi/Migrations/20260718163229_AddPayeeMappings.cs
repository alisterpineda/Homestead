using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPayeeMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payee_mappings",
                schema: "finances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NormalizationVersion = table.Column<short>(type: "smallint", nullable: false),
                    PayeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SampleRawDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TimesConfirmed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastWriterDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payee_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payee_mappings_payees_PayeeId",
                        column: x => x.PayeeId,
                        principalSchema: "finances",
                        principalTable: "payees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payee_mappings_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payee_mappings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payee_mappings_CreatedByUserId",
                schema: "finances",
                table: "payee_mappings",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payee_mappings_NormalizedKey",
                schema: "finances",
                table: "payee_mappings",
                column: "NormalizedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payee_mappings_PayeeId",
                schema: "finances",
                table: "payee_mappings",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_payee_mappings_UpdatedByUserId",
                schema: "finances",
                table: "payee_mappings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payee_mappings",
                schema: "finances");
        }
    }
}
