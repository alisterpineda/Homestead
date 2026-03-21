using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveValidFromRenameValidUntilToExpiryDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidFrom",
                schema: "documents",
                table: "documents");

            migrationBuilder.RenameColumn(
                name: "ValidUntil",
                schema: "documents",
                table: "documents",
                newName: "ExpiryDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExpiryDate",
                schema: "documents",
                table: "documents",
                newName: "ValidUntil");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidFrom",
                schema: "documents",
                table: "documents",
                type: "date",
                nullable: true);
        }
    }
}
