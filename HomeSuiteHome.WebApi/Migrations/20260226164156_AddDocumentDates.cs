using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentDate",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidFrom",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntil",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentDate",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                schema: "documents",
                table: "documents");
        }
    }
}
