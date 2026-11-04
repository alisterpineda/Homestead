# Search — design

This file covers the Documents list-page search: which fields are searched, how the query is weighted and ranked, how prefix expansion works, what filters layer on top, and how sort interacts with search. The high-level picture lives in [overview.md](overview.md); load this when working on the list query, the FTS index, the filter popover, or anything that drives the list page's primary input.

## Search-first list page

The list page treats search as its primary entry point. A single text input at the top of the page drives both *what* documents are returned and *what order* they come back in when a query is present. Filters (tags, parties, and — at the API level — date ranges and has-tags / has-parties / has-pending-suggestions) are secondary — they sit in a popover and narrow the result set; sort is also secondary and is overridden by FTS rank when a search term is active.

The user's mental model is "type words, find files." Filters refine; they do not replace the search box. Typing into the search box debounces by ~300 ms before issuing a query.

## Query mechanics

The list endpoint runs five OR'd searches against the user's query string and unions the matches:

1. **ILIKE on `Document.Name`** — case-insensitive substring match. Catches partial-word matches the FTS analyser would not pick up. Quote characters in the query are stripped before this match runs so phrase syntax doesn't degrade substring matching.
2. **ILIKE on `DocumentVersion.ExtractedText`** across any version of the document. Catches substring matches inside extracted text — useful when the FTS analyser would tokenise away the user's exact string, and the only path to find a substring buried inside a longer token. Same quote-stripping rule as above.
3. **ILIKE on `Tag.Name`** through the document's attached tags. Typing `invoices` finds documents tagged **Invoice** without the user having to open the filter popover. Case-insensitive substring match, same quote-stripping rule.
4. **ILIKE on `Party.Name`** through the document's linked parties. Typing `acme` finds documents linked to **Acme Corp**. Same matching rules.
5. **PostgreSQL full-text search** against the document's weighted `tsvector`, when the document has one. Uses `ts_rank_cd` for relevance scoring with prefix expansion — `tax` becomes `tax:*` so it matches `taxes`, `taxation`, `taxed`, etc.

A document is in the result set if any of the five modes matches. ILIKE on name and extracted text is the safety net for the FTS analyser's blind spots; tag and party ILIKE federate the search bar with the filter popover so both surfaces accept the same vocabulary; FTS is the relevance signal.

Every mode folds accents on both sides (`unaccent`, the same folding the stored vector gets — see [§Weighting](#weighting)), so "factură" finds "Factura" and the other way round, in the name, the text, tag and party names, and the full-text branch alike.

### Match modes

The five-mode predicate above is the list page's `Substring` mode, forgiving of partial typing. The list endpoint also offers a `Words` mode (`MatchMode=Words`; the MCP server's `search_documents` uses it), for callers that send whole words and want precision over tolerance:

- Every term must appear as a **whole word** in the name, a tag name, a party name or the notes (`\m`/`\M` word boundaries, case-insensitive, accents folded); a term of four or more characters also matches as the start of a word ("repai" finds "repair"). Quotes are dropped for these branches; adjacency is the full-text branch's affordance.
- Content goes through the full-text vector with the same rule: a term shorter than four characters is its exact lexeme, not a prefix (`car`, not `car:*`), so "car repair" no longer surfaces "Health **Care**", "**car**rying" or "GO **CARD**" through prefix expansion. There is no substring branch over the text.
- Notes are searched in this mode (the list page's mode does not search them).
- Ranking puts what the document **is** above what its text **mentions**: a match on the name, a tag or a party comes before a content-only match, then cover-density rank, then `UpdatedAt` and id. The list page's mode keeps its documented ranking (content rank, with association-only matches at rank 0).

Both modes share one definition per branch (`DocumentSearchTerms`), so the filter, the ranking tier and the per-row report below cannot disagree.

### Where a result matched

With a search, each item carries `MatchedOn`: which of `name`, `content`, `tag`, `party`, `notes` the query matched, in that order (null without a search). `content` means the latest version's text or the summary — the stored vector also indexes the name, so the report re-derives the name's and the text's own vectors for the page's rows rather than reading the combined one. A tag- or party-only match has no snippet (snippets come from the text), and `MatchedOn` is what explains it.

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

All three fields are **accent-folded** through `unaccent` before indexing (migration `AddUnaccentSearchFolding`), and every query side folds its terms the same way, so "factură" and "Factura" index and match as one lexeme. The vector is materialised on `Document.SearchVector` and indexed with a GIN index. It is maintained **entirely by database triggers** (migration `AddSearchVectorTriggers`, functions redefined by `AddUnaccentSearchFolding`) — there is no application-side updater. Because the indexed content spans two tables (Name and Summary live on `documents`; the latest version's ExtractedText lives on `document_versions`), a single `GENERATED ALWAYS AS … STORED` column is not sufficient, so two triggers keep the column in sync:

- A **`BEFORE INSERT OR UPDATE OF Name, Summary`** trigger on `documents` recomputes `NEW.SearchVector` in place whenever a document is created or its `Name`/`Summary` changes.
- An **`AFTER INSERT OR DELETE OR UPDATE OF ExtractedText, VersionNumber`** trigger on `document_versions` re-derives the parent document's vector whenever the latest version's extracted text changes, or a version is added or removed (which can change *which* version is the latest).

Because the database owns the column, the vector stays consistent with the document and version state regardless of which command produced the change — document creation, a name patch, a new version upload, a reprocess of the latest version, AI classification that sets the name or summary, or a suggestion decline that reverts them. EF Core is configured to **never** write the column (`SearchVector` is mapped Ignore on save), so a save that doesn't touch `Name`/`Summary` can't clobber the trigger-computed value with null.

## Filters

Filters compose with search. All of the following are AND'd against the search predicate:

- **`TagIds[]` with `IncludeChildTags`** — the selected tag ids, optionally expanded to include all descendants by path prefix. Multiple selected tags are OR'd. See [tags.md](tags.md#tag-participation-in-search) for the expansion rule.
- **`PartyIds[]`** — exact-match against the document's linked parties. Multiple selected parties are OR'd.
- **`Deleted`** — when true, the query runs against trashed documents (bypassing the default `DeletedAt IS NULL` filter); when false or null, against live documents. The list page and the trash page share the same endpoint and differ only by this flag.
- **`GroupId`** — reserved for future scoping; not surfaced in the v1 UI.
- **`DocumentDateFrom` / `DocumentDateTo`** — an inclusive date range on `DocumentDate` (a document dated exactly on either bound is included). Either end may be given alone for an open-ended range. A document with no document date never satisfies a bound, so any date filter excludes undated documents.
- **`ExpiryDateFrom` / `ExpiryDateTo`** — the same inclusive range on `ExpiryDate` ("expiring in the next 90 days" is `ExpiryDateFrom=today&ExpiryDateTo=today+90`). Documents without an expiry date never match.
- **`HasDocumentDate`** / **`HasExpiryDate`** — `true` returns only documents with that date set, `false` only those without; omitted means no filter. The complement of a date range: "what still needs a date" is `HasDocumentDate=false`.
- **`HasTags`** — `false` returns only untagged documents, `true` only documents with at least one tag; omitted means no filter.
- **`HasParties`** — the same tri-state on linked parties.
- **`HasPendingSuggestions`** — `true` returns documents with at least one AI suggestion still in the `Pending` state (the same predicate that drives each row's pending-review indicator), `false` those with none pending — a document whose suggestions are all approved or declined counts as none pending.

A range whose `From` is after its `To` is rejected with a 400 rather than silently returning nothing. All filters run on the access-scoped query before paging, so the total count and page boundaries reflect the filtered set and a filter never reveals a document the user cannot otherwise see.

The date-range, expiry-range and has-* filters (including has-document-date / has-expiry-date) are API-level in v1: the list endpoint accepts them, but the list page's filter popover does not surface them yet.

## Sort

Sort fields: `Name`, `FileSize`, `ContentType`, `DocumentDate`, `ExpiryDate`, `CreatedAt`, `UpdatedAt`, `DeletedAt`. Direction: ascending or descending. `Name` sorts **case-insensitively** ("Manulife" before "MIC", "Medical" before "MedRec"), so capitalisation never regroups the list. Under `DocumentDate` and `ExpiryDate`, documents without that date sort **last in both directions** — ascending reads "oldest / soonest first", descending "newest / furthest out first", and the undated tail never crowds the top of either. Every sort breaks ties by `UpdatedAt` (newest first) and then by id, so paging over a tie (same name, same date, the undated tail) is stable across requests. Like the has-* filters, the `ExpiryDate` sort is API-level in v1 and not yet offered in the list page's sort menu.

When the user has typed a search term *and has not picked an explicit sort*, results are ordered by FTS rank — the most relevant matches come first. This is the default: the search-first design means a bare query implies "show me what's most relevant." If the user has explicitly chosen a sort field, that choice is respected and persists through the search (it is not overridden by rank); clearing the search leaves the chosen sort in place. When the search box is empty, the user's chosen sort (or the `UpdatedAt` default) applies.

In other words, rank ordering is the *default* during search, not an absolute override — an explicit sort always wins. This behaviour is consistent across the live list and the trash list.

## "Did you mean…?" suggestions

When the primary search returns zero results, the list endpoint runs a second, narrow query that finds document, tag, and party names that are *close* to the typed query and surfaces them as explicit "Did you mean…?" chips above the empty-state. The user clicks a chip to retry the search with the suggested name; nothing is silently re-ranked or auto-corrected.

The suggestion query uses PostgreSQL `pg_trgm` `word_similarity()` against three sources — `Document.Name`, `Tag.Name`, and `Party.Name` — each scoped to documents the user can already see. `word_similarity` finds the best-matching continuous extent of trigrams within a name, which tolerates typos against long filenames (e.g., `mazdo` → `Mazda Receipt 2023.pdf`) where plain `similarity()` would score below the threshold because of the surrounding text. The threshold (0.3) is calibrated so single-letter mid-word typos still surface; unrelated strings score well below it and produce no chips. The three result sets are unioned and **deduplicated by name** — a name keeps its highest score across the sources — and the top five by descending score are returned.

Rules:

- **Only when there are zero results.** A successful primary match never shows suggestions — they are a recovery affordance, not an always-on signal.
- **Only when the query is at least three characters.** Two-character queries produce too much trigram noise to be useful.
- **Filters apply.** Group, party, tag, date-range, expiry-range, has-tags / has-parties / has-pending-suggestions, and trash scoping run on the suggestion query exactly as they do on the primary query — clicking a chip never produces a "ghost" result that disappears on retry because the filter excluded it.
- **Federated over `Document.Name`, `Tag.Name`, and `Party.Name`.** All three are sources of suggestions (extracted text is not). A typo against a tag or party name surfaces the closest tag/party name directly — not only via an overlapping document name — and clicking the chip re-runs the primary search, which federates over those same names.
- **Distinct.** A name that appears more than once — two documents with the same name, or a document name that coincides with a tag name — produces one chip, not several.
- **Typed.** `Suggestions` is the list of names (the chips); `SuggestionDetails` carries the same names with where each comes from (`document`, `tag` or `party` — the best-scoring source when a name is both), so a caller that acts on a suggestion knows whether to retry a search or pick a filter.

Trigram GIN indexes on `Document.Name`, `Tag.Name`, and `Party.Name` (the latter two added in migration `AddTagAndPartyNameTrigramIndexes`) accelerate the suggestion query; the `Document.Name` index additionally backs the primary `ILIKE` substring match described above.

## Index maintenance and direct edits

`Document.SearchVector` is maintained entirely by database triggers (see [§Weighting](#weighting)), so a direct database edit to a document's `Name`/`Summary` or to a version's `ExtractedText`/`VersionNumber` — even one made outside the application's command handlers — keeps the search vector consistent automatically. The audit log, however, only captures changes made *through* the command handlers: a direct database edit is not audited even though the vector stays correct. The command handlers therefore remain the only supported way to mutate documents.

In practice direct edits only happen during migrations or operator interventions; ordinary use stays consistent on both fronts.
