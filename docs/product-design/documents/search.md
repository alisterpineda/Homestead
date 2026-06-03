# Search — design

This file covers the Documents list-page search: which fields are searched, how the query is weighted and ranked, how prefix expansion works, what filters layer on top, and how sort interacts with search. The high-level picture lives in [overview.md](overview.md); load this when working on the list query, the FTS index, the filter popover, or anything that drives the list page's primary input.

## Search-first list page

The list page treats search as its primary entry point. A single text input at the top of the page drives both *what* documents are returned and *what order* they come back in when a query is present. Filters (tags, parties) are secondary — they sit in a popover and narrow the result set; sort is also secondary and is overridden by FTS rank when a search term is active.

The user's mental model is "type words, find files." Filters refine; they do not replace the search box. Typing into the search box debounces by ~300 ms before issuing a query.

## Query mechanics

The list endpoint runs five OR'd searches against the user's query string and unions the matches:

1. **ILIKE on `Document.Name`** — case-insensitive substring match. Catches partial-word matches the FTS analyser would not pick up. Quote characters in the query are stripped before this match runs so phrase syntax doesn't degrade substring matching.
2. **ILIKE on `DocumentVersion.ExtractedText`** across any version of the document. Catches substring matches inside extracted text — useful when the FTS analyser would tokenise away the user's exact string, and the only path to find a substring buried inside a longer token. Same quote-stripping rule as above.
3. **ILIKE on `Tag.Name`** through the document's attached tags. Typing `invoices` finds documents tagged **Invoice** without the user having to open the filter popover. Case-insensitive substring match, same quote-stripping rule.
4. **ILIKE on `Party.Name`** through the document's linked parties. Typing `acme` finds documents linked to **Acme Corp**. Same matching rules.
5. **PostgreSQL full-text search** against the document's weighted `tsvector`, when the document has one. Uses `ts_rank_cd` for relevance scoring with prefix expansion — `tax` becomes `tax:*` so it matches `taxes`, `taxation`, `taxed`, etc.

A document is in the result set if any of the five modes matches. ILIKE on name and extracted text is the safety net for the FTS analyser's blind spots; tag and party ILIKE federate the search bar with the filter popover so both surfaces accept the same vocabulary; FTS is the relevance signal.

The tag/party clauses are deliberately **federated** rather than baked into the `tsvector`. Including tag and party names in the stored vector would force a re-index of every linked document on each tag rename or tag/party add-remove. Renames are common, so the join-time match keeps the index pure at the cost of a wider OR predicate at query time. Access scope is preserved automatically: the join navigates from documents the user can already see, so a user never matches a tag attached to a sibling household member's document. Ranking is unaffected — `ts_rank_cd` returns 0 for documents matched only via tag or party name, so direct content matches outrank association matches under relevance sort.

### Quoted phrase search

The FTS branch parses `"…"` substrings as adjacency phrases. Inside quotes, words must appear in order with no intervening tokens; outside quotes, the prefix-AND behaviour is unchanged.

- `tax invoice` — both terms must appear, anywhere, in either order. (`tax:* & invoice:*`)
- `"operating agreement"` — the two words must appear adjacently, in that order. (`(operating <-> agreement)`)
- `"operating agreement" 2024` — adjacency phrase AND prefix term. (`(operating <-> agreement) & 2024:*`)

Phrases are tolerant rather than strict: English stopwords (`the, a, an, of, and, or, to, in, on, at, for`) are dropped from inside a phrase before the followed-by chain is built. `"the cat in the hat"` matches the content-word adjacency pattern (`cat <-> hat`) rather than enforcing exact word distance. This is a deliberate v1 simplification — `to_tsquery` does not handle stopwords gracefully inside `<->` chains, so stripping them ourselves keeps the common case working at the cost of perfect adjacency for stopword-heavy queries.

Edge cases degrade gracefully:

- An empty phrase (`""`), a whitespace-only phrase, or a phrase that contains nothing but stopwords contributes no FTS clause — the query falls back to the other branches.
- Stray unmatched quotes are stripped and the surrounding text is treated as ordinary prefix terms; `unmatched"quote` searches for `unmatched:* & quote:*`.
- tsquery operator characters (`& | ! ( ) : * \ '`) and `<` / `>` inside the query are sanitized out before composition, so a query like `"foo&&|bar":*` cannot inject operators or distance modifiers and never returns a 500.

The phrase parser is the only FTS-side affordance. Quotes are stripped from the input the ILIKE branches and the trigram suggestion path see, so phrase syntax does not degrade substring matching or "Did you mean…?" recovery — but those branches do not honour adjacency. Ranking weights are unaffected.

## Weighting

The `tsvector` index combines three fields with explicit weights:

- **Weight A**: `Document.Name`. Hits in the document name rank highest.
- **Weight B**: the *latest* `DocumentVersion.ExtractedText`. Hits in file content rank lower than name hits.
- **Weight C**: `Document.Summary`. Hits in the AI-generated (or manually entered) summary rank below extracted text. This enriches search results with semantically dense content without dominating relevance.

Older versions' extracted text does not contribute to the search vector. This keeps the index small and ensures the document's "current" identity drives ranking. Reprocessing a version that happens to be the latest version updates the search vector; reprocessing an older version does not.

The vector is materialised on `Document.SearchVector` and indexed with a GIN index. It is rewritten by `SearchVectorUpdater` on every operation that could change the indexed content:

- Document creation (after the upload job extracts text).
- Document name patch.
- New version upload (against the new latest version).
- Version reprocess that changes the extracted text.
- AI classification (which may set or change the document's name and summary).
- Suggestion decline that reverts a name or clears a summary.

Updates happen as part of the same job or handler that produced the new content, so the search index is consistent with the version state by the time the operation completes. The application code is the only writer — there is no database trigger maintaining the column.

## Filters

Filters compose with search. All of the following are AND'd against the search predicate:

- **`TagIds[]` with `IncludeChildTags`** — the selected tag ids, optionally expanded to include all descendants by path prefix. Multiple selected tags are OR'd. See [tags.md](tags.md#tag-participation-in-search) for the expansion rule.
- **`PartyIds[]`** — exact-match against the document's linked parties. Multiple selected parties are OR'd.
- **`Deleted`** — when true, the query runs against trashed documents (bypassing the default `DeletedAt IS NULL` filter); when false or null, against live documents. The list page and the trash page share the same endpoint and differ only by this flag.
- **`GroupId`** — reserved for future scoping; not surfaced in the v1 UI.

Date-based filtering (e.g., "documents with DocumentDate between X and Y") is not currently exposed — date fields are sortable but not filterable.

## Sort

Sort fields: `Name`, `FileSize`, `ContentType`, `DocumentDate`, `CreatedAt`, `UpdatedAt`, `DeletedAt`. Direction: ascending or descending. (`ExpiryDate` is a displayable document field but is not currently a sort option.)

When the user has typed a search term *and has not picked an explicit sort*, results are ordered by FTS rank — the most relevant matches come first. This is the default: the search-first design means a bare query implies "show me what's most relevant." If the user has explicitly chosen a sort field, that choice is respected and persists through the search (it is not overridden by rank); clearing the search leaves the chosen sort in place. When the search box is empty, the user's chosen sort (or the `UpdatedAt` default) applies.

In other words, rank ordering is the *default* during search, not an absolute override — an explicit sort always wins. This behaviour is consistent across the live list and the trash list.

## "Did you mean…?" suggestions

When the primary search returns zero results, the list endpoint runs a second, narrow query over `Document.Name` to find names that are *close* to the typed query and surfaces them as explicit "Did you mean…?" chips above the empty-state. The user clicks a chip to retry the search with the suggested name; nothing is silently re-ranked or auto-corrected.

The suggestion query uses PostgreSQL `pg_trgm` `word_similarity()` against the document name. `word_similarity` finds the best-matching continuous extent of trigrams within the name, which tolerates typos against long filenames (e.g., `mazdo` → `Mazda Receipt 2023.pdf`) where plain `similarity()` would score below the threshold because of the surrounding text. The threshold is calibrated so single-letter mid-word typos still surface; unrelated strings score well below the threshold and produce no chips. Up to five distinct names are returned, ordered by descending score.

Rules:

- **Only when there are zero results.** A successful primary match never shows suggestions — they are a recovery affordance, not an always-on signal.
- **Only when the query is at least three characters.** Two-character queries produce too much trigram noise to be useful.
- **Filters apply.** Group, party, tag, and trash scoping run on the suggestion query exactly as they do on the primary query — clicking a chip never produces a "ghost" result that disappears on retry because the filter excluded it.
- **Source is `Document.Name` only.** Extracted text, tag names, and party names are not sources of suggestions. The primary search already federates over tag and party names, so a typo against a tag still matches via the trigram suggestion path against any document name that happens to overlap; dedicated fuzzy suggestions over tag/party names are a follow-up that would benefit from trigram indexes on those columns.
- **Distinct.** Two documents with the same name produce one chip, not two.

The trigram GIN index on `Document.Name` (added alongside this feature) accelerates both the suggestion query and the primary `ILIKE` substring match described above.

## Index maintenance and direct edits

`Document.SearchVector` is maintained by application code only. A direct database edit to a document's name or to a version's extracted text — outside the application's command handlers — leaves the search vector stale. The audit log will not capture such edits either; the application's command handlers are the only supported way to mutate documents.

In practice this only matters for migrations or operator interventions; ordinary use stays consistent.
