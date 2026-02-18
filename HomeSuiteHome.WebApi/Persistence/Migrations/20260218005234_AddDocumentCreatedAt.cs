using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "documents",
                table: "documents");
        }
    }
}
