# Search — design

This file covers the Documents list-page search: which fields are searched, how the query is weighted and ranked, how prefix expansion works, what filters layer on top, and how sort interacts with search. The high-level picture lives in [overview.md](overview.md); load this when working on the list query, the FTS index, the filter popover, or anything that drives the list page's primary input.

## Search-first list page

The list page treats search as its primary entry point. A single text input at the top of the page drives both *what* documents are returned and *what order* they come back in when a query is present. Filters (tags, parties) are secondary — they sit in a popover and narrow the result set; sort is also secondary and is overridden by FTS rank when a search term is active.

The user's mental model is "type words, find files." Filters refine; they do not replace the search box. Typing into the search box debounces by ~300 ms before issuing a query.

## Query mechanics

The list endpoint runs three OR'd searches against the user's query string and unions the matches:

1. **ILIKE on `Document.Name`** — case-insensitive substring match. Catches partial-word matches the FTS analyser would not pick up.
2. **ILIKE on `DocumentVersion.ExtractedText`** across any version of the document. Catches substring matches inside extracted text — useful when the FTS analyser would tokenise away the user's exact string, and the only path to find a substring buried inside a longer token.
3. **PostgreSQL full-text search** against the document's weighted `tsvector`, when the document has one. Uses `ts_rank_cd` for relevance scoring with prefix expansion — `tax` becomes `tax:*` so it matches `taxes`, `taxation`, `taxed`, etc.

A document is in the result set if any of the three modes matches. ILIKE is the safety net for the FTS analyser's blind spots; FTS is the relevance signal.

## Weighting

The `tsvector` index combines two fields with explicit weights:

- **Weight A**: `Document.Name`. Hits in the document name rank highest.
- **Weight B**: the *latest* `DocumentVersion.ExtractedText`. Hits in file content rank lower than name hits.

Older versions' extracted text does not contribute to the search vector. This keeps the index small and ensures the document's "current" identity drives ranking. Reprocessing a version that happens to be the latest version updates the search vector; reprocessing an older version does not.

The vector is materialised on `Document.SearchVector` and indexed with a GIN index. It is rewritten by `SearchVectorUpdater` on every operation that could change the indexed content:

- Document creation (after the upload job extracts text).
- Document name patch.
- New version upload (against the new latest version).
- Version reprocess that changes the extracted text.

Updates happen as part of the same job or handler that produced the new content, so the search index is consistent with the version state by the time the operation completes. The application code is the only writer — there is no database trigger maintaining the column.

## Filters

Filters compose with search. All of the following are AND'd against the search predicate:

- **`TagIds[]` with `IncludeChildTags`** — the selected tag ids, optionally expanded to include all descendants by path prefix. Multiple selected tags are OR'd. See [tags.md](tags.md#tag-participation-in-search) for the expansion rule.
- **`PartyIds[]`** — exact-match against the document's linked parties. Multiple selected parties are OR'd.
- **`Deleted`** — when true, the query runs against trashed documents (bypassing the default `DeletedAt IS NULL` filter); when false or null, against live documents. The list page and the trash page share the same endpoint and differ only by this flag.
- **`GroupId`** — reserved for future scoping; not surfaced in the v1 UI.

Date-based filtering (e.g., "documents with DocumentDate between X and Y") is not currently exposed — date fields are sortable but not filterable.

## Sort

Sort fields: `CreatedAt`, `UpdatedAt`, `DeletedAt`, `Name`, `DocumentDate`, `ExpiryDate`. Direction: ascending or descending.

When the user has typed a search term, sort is overridden by FTS rank — the most relevant matches come first regardless of the chosen sort field. When the search box is empty, the user's chosen sort applies.

The "search hides sort" rule is intentional: the search-first design means a query implies "show me what's most relevant," and any other ordering would defeat that. The rule is consistent across the live list and the trash list.

## "Did you mean…?" suggestions

When the primary search returns zero results, the list endpoint runs a second, narrow query over `Document.Name` to find names that are *close* to the typed query and surfaces them as explicit "Did you mean…?" chips above the empty-state. The user clicks a chip to retry the search with the suggested name; nothing is silently re-ranked or auto-corrected.

The suggestion query uses PostgreSQL `pg_trgm` `word_similarity()` against the document name. `word_similarity` finds the best-matching continuous extent of trigrams within the name, which tolerates typos against long filenames (e.g., `mazdo` → `Mazda Receipt 2023.pdf`) where plain `similarity()` would score below the threshold because of the surrounding text. The threshold is calibrated so single-letter mid-word typos still surface; unrelated strings score well below the threshold and produce no chips. Up to five distinct names are returned, ordered by descending score.

Rules:

- **Only when there are zero results.** A successful primary match never shows suggestions — they are a recovery affordance, not an always-on signal.
- **Only when the query is at least three characters.** Two-character queries produce too much trigram noise to be useful.
- **Filters apply.** Group, party, tag, and trash scoping run on the suggestion query exactly as they do on the primary query — clicking a chip never produces a "ghost" result that disappears on retry because the filter excluded it.
- **Source is `Document.Name` only in v1.** Extracted text, tag names, and party names are not yet sources of suggestions. Names are the smallest, highest-signal corpus and the most common typo target. The other gaps are tracked separately.
- **Distinct.** Two documents with the same name produce one chip, not two.

The trigram GIN index on `Document.Name` (added alongside this feature) accelerates both the suggestion query and the primary `ILIKE` substring match described above.

## Index maintenance and direct edits

`Document.SearchVector` is maintained by application code only. A direct database edit to a document's name or to a version's extracted text — outside the application's command handlers — leaves the search vector stale. The audit log will not capture such edits either; the application's command handlers are the only supported way to mutate documents.

In practice this only matters for migrations or operator interventions; ordinary use stays consistent.
