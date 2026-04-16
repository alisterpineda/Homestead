using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class MakeUpdatedAtNonNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing NULL UpdatedAt values with CreatedAt before making columns NOT NULL
            migrationBuilder.Sql("""UPDATE settings."text_extraction_engines" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE documents."tags" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE documents."parties" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE auth."groups" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE documents."documents" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE documents."document_comments" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");
            migrationBuilder.Sql("""UPDATE settings."app_settings" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" IS NULL""");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "settings",
                table: "text_extraction_engines",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "tags",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "parties",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "auth",
                table: "groups",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "document_versions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            // Backfill document_versions UpdatedAt with CreatedAt
            migrationBuilder.Sql("""UPDATE documents."document_versions" SET "UpdatedAt" = "CreatedAt" """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "document_comments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "settings",
                table: "app_settings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "documents",
                table: "document_versions");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "settings",
                table: "text_extraction_engines",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "tags",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "parties",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "auth",
                table: "groups",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "documents",
                table: "document_comments",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "settings",
                table: "app_settings",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");
        }
    }
}
