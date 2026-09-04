using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class DropInventoryItemNoteSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fold any existing subject into the body before the column goes, so a "subject-only" note
            // (an empty body was allowed alongside a subject) keeps its text. The separator is a
            // single-line " — " rather than a newline: the body is now one line of text and the
            // composer is a text <input>, which strips line breaks. LEFT() keeps a 200-char subject
            // plus a 4000-char body inside the column's varchar(4000) instead of aborting the
            // migration (and with it, startup) on an over-length row.
            migrationBuilder.Sql("""
                UPDATE inventory.item_notes
                SET "Body" = LEFT(CASE WHEN "Body" = '' THEN "Subject" ELSE "Subject" || ' — ' || "Body" END, 4000)
                WHERE "Subject" IS NOT NULL;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_item_notes_subject_or_body",
                schema: "inventory",
                table: "item_notes");

            migrationBuilder.DropColumn(
                name: "Subject",
                schema: "inventory",
                table: "item_notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subject",
                schema: "inventory",
                table: "item_notes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_item_notes_subject_or_body",
                schema: "inventory",
                table: "item_notes",
                sql: "\"Subject\" IS NOT NULL OR \"Body\" <> ''");
        }
    }
}
