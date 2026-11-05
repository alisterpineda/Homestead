using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <summary>
    /// A text search configuration that folds accents itself: <c>documents.english_unaccent</c> is <c>english</c> with
    /// the <c>unaccent</c> dictionary in front of the stemmer for every accent-bearing token type. The vector triggers
    /// (AddSearchVectorTriggers, folded by AddUnaccentSearchFolding) and every query side use it instead of wrapping
    /// text in <c>unaccent()</c>, so <c>ts_headline</c> can run over the original text — a snippet shows "Hydro-Québec",
    /// not "Hydro-Quebec" — while still matching an accented word. The two trigger functions are redefined and every
    /// vector recomputed once; the GIN index stays.
    /// </summary>
    public partial class AddUnaccentTextSearchConfiguration : Migration
    {
        public const string Configuration = "documents.english_unaccent";

        // The vector expression under the configuration (DocumentSearchTerms.TsLanguage names the same configuration
        // on the query side). No unaccent() call: the configuration folds each lexeme.
        private const string ConfiguredVector =
            """
                setweight(to_tsvector('documents.english_unaccent', COALESCE({0}."Name", '')), 'A') ||
                setweight(to_tsvector('documents.english_unaccent', COALESCE(
                    (SELECT dv."ExtractedText"
                     FROM documents.document_versions dv
                     WHERE dv."DocumentId" = {0}."Id"
                     ORDER BY dv."VersionNumber" DESC
                     LIMIT 1), '')), 'B') ||
                setweight(to_tsvector('documents.english_unaccent', COALESCE({0}."Summary", '')), 'C')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // asciiword tokens carry no accents, so only the token types that can (word, hword, hword_part) get the
            // filtering dictionary; the mapping otherwise matches english.
            migrationBuilder.Sql(
                """
                CREATE TEXT SEARCH CONFIGURATION documents.english_unaccent (COPY = pg_catalog.english);
                ALTER TEXT SEARCH CONFIGURATION documents.english_unaccent
                    ALTER MAPPING FOR hword, hword_part, word WITH public.unaccent, english_stem;
                """);

            AddUnaccentSearchFolding.ReplaceVectorFunctions(migrationBuilder, ConfiguredVector);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            AddUnaccentSearchFolding.ReplaceVectorFunctions(migrationBuilder, AddUnaccentSearchFolding.FoldedVector);

            migrationBuilder.Sql("DROP TEXT SEARCH CONFIGURATION documents.english_unaccent;");
        }
    }
}
