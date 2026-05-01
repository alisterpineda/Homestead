# Commodities & Prices — Reference

Detailed design for commodity and price management in the Finances module. Expands on the summaries in [overview.md](overview.md#commodities) and [overview.md](overview.md#prices). Read this before designing, implementing, or reviewing any commodity- or price-related functionality — commodity creation/editing, the ISO 4217 currency seed, the exchange qualifier, price sources, carry-forward rules, FX chaining, or the commodity/price UI.

## Conceptual model

A **commodity** is anything that has a quantity and can be held in an account — a currency (CAD, USD, GBP, ...) or a security (VEQT, AAPL, ...). Currencies and securities live in one table; only behaviour in certain transaction shapes differs.

A **price** is a single observation of a commodity's value at a specific instant, attributed to a **source**: the tuple `(commodity, quote currency, priceAt, source, rate)`, read as *"according to `source`, 1 unit of `commodity` was worth `rate` units of `quote currency` at `priceAt`."* Prices are point observations, not intervals. Missing points resolve via carry-forward (see *Lookup and carry-forward*).

Prices are captured forward from the user's earliest recorded activity. There is no backfill for dates before the user began tracking.

## Commodity kinds

| Kind     | Meaning                              | Exchange required | User-creatable |
|----------|--------------------------------------|-------------------|----------------|
| Currency | Fiat or otherwise fungible currency  | No                | No (seeded)    |
| Security | Stock, ETF, mutual fund, crypto, ... | Yes               | Yes            |

**Immutable after creation:** Symbol, Exchange, Kind. These define the identity of the commodity; changing them would silently re-interpret historical data.

**Editable:** Name, DisplayPrecision.

## ISO 4217 currency seed

All ISO 4217 active currencies are seeded on database initialisation with `Kind=Currency`, `Exchange=null`, and `DisplayPrecision` set to the ISO minor unit. Seeded commodities carry `IsSystemManaged=true` and are **fully read-only**: they cannot be renamed, re-precision'd, or deleted through the API or the UI. New codes added upstream are picked up on next startup. Removed codes are not purged.

## Exchange qualifier (securities)

Securities carry a free-text `Exchange` string, required and non-empty. Examples: `NASDAQ`, `TSX`, `LSE`, `OTC`, `CRYPTO`. Values are uppercase-normalised on write and ≤16 characters. No closed list — users working with delisted exchanges, private funds, or obscure venues use any label they want.

The same symbol on different exchanges is two different commodities (e.g., `AAPL · NASDAQ` and `AAPL · OTHER`). Merging is not supported — delete the unused one if needed.

## Commodity uniqueness

- Currencies: `UNIQUE(Symbol)`, with `Exchange` always null.
- Securities: `UNIQUE(Symbol, Exchange)`.

`Symbol` is uppercase-normalised and trimmed on write.

## Creation flow (securities only)

Single modal opened from `/finances/commodities` via the persistent `+ New security` toolbar CTA. Kind is always `Security` — currencies are never user-created. Fields:

1. **Symbol** — required, ≤32 chars, uppercase, trimmed.
2. **Exchange** — required, ≤16 chars, uppercase, trimmed.
3. **Name** — required, ≤256 chars.
4. **Display precision** — required, integer 0–10, default 4.

Inline creation from transaction flows (e.g., `Add position`) is deferred to v2 — in v1 users must create the security in settings first.

## Edit / Delete

**Edit (modal):**
- Editable: Name, DisplayPrecision.
- Immutable: Symbol, Exchange, Kind — shown but disabled.

**Delete:** allowed only if the commodity has zero references anywhere — no account uses it as reporting currency, no split references it (as `Commodity` or as `UnitPriceCommodity`), no price references it (as `Commodity` or `QuoteCurrency`). Otherwise delete is hidden. Seeded currencies are never deletable.

## List UI

Route: `/finances/commodities` (top-level under `/finances/`, sibling of `/finances/accounts` and `/finances/settings`).

Single sortable table following the cross-cutting `table-ux.md` toolbar pattern: leading search + Filter popover, trailing persistent `+ New security` CTA.

**Toolbar:**
- **Leading:** debounced search (Symbol or Name, 300ms) and a Filter popover.
- **Filter popover** carries a single `Kind` facet — checkbox group with `Security` and `Currency`. The trigger label transforms with the active selection: `Filter: Securities` (default), `Filter: Currencies`, `Filter: All kinds`, or bare `Filter` if neither is checked (treated as show-all). A Reset button in the popover header restores the default.
- **Default Kind filter = `Security`.** Preserves the original "currencies drown out the few securities a household tracks" rationale; currencies remain one click away through the popover. The `Filter: Securities` trigger label is the discoverability mitigation — it tells the user the view is scoped without making them open the popover to find out.
- **Trailing:** persistent `+ New security` CTA, visible regardless of the active Kind filter (currencies are never user-creatable, so the CTA semantics never depend on what's currently shown).

**Columns** (always present — no adaptive hiding when Kind toggles, to avoid layout reflow):

| Column | Sortable | Currency rendering |
|---|---|---|
| Symbol | yes | as-is |
| Kind | yes | `Security` / `Currency` badge |
| Exchange | yes | `—` (sortUndefined: 'last') |
| Name | yes | as-is |
| Display precision | yes | as-is |
| # prices | yes | `—` (sortUndefined: 'last') |
| Latest price | yes | `—` (sortUndefined: 'last') — relative-age formatter for securities |

Every column uses `Symbol asc` as a stable secondary sort axis so row order stays deterministic across re-fetches.

**Row interaction.** Clicking a row navigates to `/finances/commodities/:id`. The row-actions cell stops propagation. Row actions are Edit and Delete (when `canDelete`); both are hidden entirely on system-managed (seeded) rows.

**State.** Filter, sort, and search are URL-encoded — `?kind=Security|Currency|all|none&search=…&sortBy=…&sortDir=…` — and a per-route `CommodityListStateService` restores the last-used params when the user returns from a detail page.

**Fetch shape.** With ~150 active ISO 4217 currencies plus a handful of securities, the full set fits in a single client-side fetch (no `Kind` parameter sent; client-side filter and TanStack sort). A runtime guard logs a warning if `totalCount` exceeds the page size so we catch growth before users do.

The list endpoint projects the per-row aggregates the table needs — `PriceCount`, `LatestPriceAt`, and `CanDelete` — onto `CommodityResponse` so the table renders in a single round-trip. `CanDelete` mirrors the same reference predicate `DeleteCommodityHandler` enforces (account reporting currency, splits, split price annotations, price rows on either side); the row's Delete affordance hides when it's false. Seeded currencies always have `CanDelete=false` and `IsSystemManaged=true`, so they render with no row-actions kebab at all.

## Commodity detail

Route: `/finances/commodities/:id`.

**Header:** breadcrumb · symbol · exchange badge (securities only) · name · kind.

**Tabs:**

- **Info** — metadata: symbol, exchange (securities only), name, kind, display precision, system-managed badge.
- **Prices** — chronological list of price observations across all sources. The table has a `Source` column (filterable) so the user can inspect what each provider has contributed; the row currently winning the lookup at "now" gets a subtle marker. Section header above the table carries the *New manual price* action (which always writes a `Manual`-source row), rendered with the table body — not in the page-header strip — so the header stays stable as the user toggles tabs.

Seeded currencies render read-only throughout.

## Prices — model

```
CommodityPrice {
  Id: Guid
  CommodityId: Guid              -> Commodity
  QuoteCurrencyId: Guid          -> Commodity (must be Kind=Currency)
  PriceAt: DateTimeOffset        (stored UTC; uniqueness axis)
  Rate: numeric                  (> 0)
  Source: CommodityPriceSource   (enum; stored as string)
  CreatedByTransactionId: Guid?  -> AccountTransaction (set iff Source == Transaction)
  audit fields
}
```

**Uniqueness:** `UNIQUE(CommodityId, QuoteCurrencyId, PriceAt, Source)` — enforced by the database. Two sources observing the same pair at the same instant is allowed; the lookup resolves the tie (see *Lookup and carry-forward*).

**Rate validation:** `Rate > 0` — enforced at the API layer and by database constraint.

**Self-prices:** rejected. A row with `CommodityId == QuoteCurrencyId` is meaningless and fails validation.

**Quote must be a currency:** `QuoteCurrency.Kind == Currency`. Securities can be quoted in any currency, but only currencies can serve as the quote side.

## Price sources — model

A **source** identifies the origin of a price observation. Sources are a code-defined enum — adding a new source is a code release (a new enum value plus an `IPriceProvider` implementation registered against it). The runtime model is just the enum value on each `CommodityPrice` row, plus a per-source settings row for the external sources that have configurable knobs.

```csharp
public enum CommodityPriceSource
{
    Transaction,    // system; always-on; no settings row
    Manual,         // system; always-on; no settings row
    BankOfCanada,   // external
    // future provider keys added here
}
```

Each enum value falls into one of three **kinds** (a property of the value, not a stored column):

| Kind | Values | How rows are created |
|---|---|---|
| `Transaction` | `Transaction` | Auto-emitted from annotated splits — see *Which transaction shapes create price rows*. Rows have `CreatedByTransactionId` set. |
| `Manual` | `Manual` | Created via *New manual price* on the commodity detail page. `CreatedByTransactionId` is null. |
| `External` | everything else | Imported by a background job — see *Background import jobs*. |

**System values** (`Transaction`, `Manual`) are always enabled, never disable-able, and have no configuration. They have **no settings row** — the lookup just always considers them in scope.

**External values** carry their configurable knobs in a settings table:

```
CommodityPriceSourceSettings {
  Source: CommodityPriceSource    (PK; one row per External enum value)
  IsEnabled: bool                 (default false on seed)
  Config: jsonb?                  (provider-specific; schema declared by the IPriceProvider impl)
  PairAllowList: jsonb?           (opt-in fixed list — see Import scope)
  LastImportAt: DateTimeOffset?
  LastImportStatus: string?       (Success | PartialSuccess | Failed | NeverRun)
  LastImportMessage: string?      (free-form on failed or partially-successful runs; null on Success)
  audit fields
}
```

**Seeding.** On startup, the application ensures a settings row exists for every External enum value not already in the table — defaulting to `IsEnabled=false`. New providers added in code are picked up on next startup. Enum values that have been removed from code but still have orphan settings rows are ignored (no foreign key, so they don't block anything); a maintenance migration prunes them when convenient.

**Disabling an external source** (`IsEnabled=false` on its settings row) hides all of its prices from valuation lookup *and* stops new imports. Re-enabling restores its rows to the lookup pool — the rows themselves stay in storage while disabled. Enable/disable is the lever for "treat this source as if it weren't here." There is no separate "pause imports but keep prices live" mode — that would silently widen carry-forward gaps without the admin noticing.

**There is no "delete source" action.** A source exists as long as its enum value exists in code. To stop using a source, disable it. Removing an enum value is a deploy-time decision, paired with a migration that decides what to do with its historical rows (typically: leave them in place, since they remain valid observations even after the provider stops being maintained).

## Price time semantics

`PriceAt` is a `DateTimeOffset`, stored UTC. This gives well-defined ordering for same-day observations, alignment with transaction timestamps (see the *Transactions* section in [overview.md](overview.md#transactions)), and a single axis for carry-forward lookups.

Transaction-linked prices inherit the source transaction's `OccurredAt`. Manual entry collects a date with an optional time; the frontend defaults the time to `00:00 UTC` on the entered date when the user only picks a date. The API accepts a raw `PriceAt: DateTimeOffset` — the server does not default or normalise the time. On a day that has both a 00:00-UTC manual price and a 12:00-UTC transaction-linked price, carry-forward at instants ≥ 12:00 UTC picks the transaction-linked row (most recent `PriceAt ≤ T`).

Daily-resolution providers (e.g., end-of-day FX or stock close) store `PriceAt` as **midnight UTC of the trade date**. Carry-forward semantics make the exact instant largely irrelevant — anything in the trade-day window resolves to that day's observation — and the convention keeps providers from having to know each exchange's local close time and timezone.

`PriceAt` may be up to **one day ahead** of current UTC — the server rejects timestamps further in the future, which avoids accidentally staging prices for arbitrary dates.

## Per-row source semantics

A row's source determines its edit/delete affordances in the prices UI.

**Transaction-source rows** (`Source == Transaction`) are **read-only**. The row is labelled with the source name and has no edit or delete affordance. The label carries a tooltip explaining that the price is edited via its source transaction. (A clickable deep-link to the originating transaction is deferred to a later pass — there is no per-transaction route today, and resolving the price's account context for navigation needs an extra fetch the v1 prices tab does not perform.) The row is materialised from a **split price annotation** (`Split.UnitPriceRate` + `Split.UnitPriceCommodityId`) on the source transaction — every annotated split emits exactly one `CommodityPrice` row. Editing the annotation updates the row in place; deleting the transaction deletes the row.

**Manual-source rows** (`Source == Manual`): only **Rate** is editable after creation. Changing Commodity, Quote Currency, or `PriceAt` is conceptually a new observation — delete and re-add.

**External-source rows** (`Source` is any External enum value) are **read-only** in the prices UI. They are managed via the source's settings row (see *Source management UI*) — the user does not edit individual imported observations. To override an imported (or transaction-derived) price the user adds a `Manual` row at the same instant; cross-source rows at the same `PriceAt` are allowed everywhere and the lookup tiebreaker prefers `Manual` (see *Lookup and carry-forward*).

**Security** opening balances on an Investment account (via Add Position) *may* carry a price annotation on the security split at `OccurredAt` = asOfDate @ 12:00 UTC — the per-unit price is **optional**. If supplied, the security leg is annotated `@ price reportingCurrency` and the annotation materialises as a linked price row; the transaction balances per-currency via the annotation (asset +qty VEQT @ price ↔ equity -price×qty CAD). If omitted, the position is captured unpriced: both splits are in the security commodity, the transaction balances per-commodity (asset +qty VEQT ↔ equity -qty VEQT), no price row is emitted, and the holding renders via carry-forward at net-worth time (or as *unpriced* if no price ever resolves). This lets users assert "I had 100 VEQT on this date" without forcing them to pin down a historical per-share price; pricing is a separable concern. Cash opening balances never emit a price row (both splits same currency — per-commodity balance, no annotation). When a security opening balance is later updated, the annotation moves with it (rate in place, or `PriceAt` on date change — rejecting if another row already occupies the target `(commodity, quote, at, source)` slot); an update that first supplies a price to a previously-unpriced opening balance adds an annotation to the security leg and creates a new linked row (the opposite-side split's commodity flips from the security to the reporting currency as a consequence). Deleting the opening balance cascades the linked row via FK.

## Which transaction shapes create price rows

A price row is created whenever a split carries a price annotation. The rule is one-to-one: every annotated split → one `CommodityPrice` row at `(Split.Commodity, Split.UnitPriceCommodityId, Transaction.OccurredAt, Split.UnitPriceRate)`. Shapes without any annotated splits never create price rows.

| Shape                       | Annotated leg            | Notes                                             |
|-----------------------------|--------------------------|---------------------------------------------------|
| Opening balance (cash)      | None                     | Single-commodity transaction — per-commodity balance. |
| Opening balance (security)  | Security leg (optional)  | Via Add position. Annotated only when the user supplies a per-unit price. Unpriced → both splits in the security, balances per-commodity. |
| Add position (security)     | Security leg (optional)  | Same as above. |
| Add position (cash)         | None                     | FX cash in a different-currency Investment account still balances per-commodity (same cash currency on both splits). |
| Investment buy              | Security leg             | `(security, funding currency, at, per-unit price)` |
| Investment sell             | Security leg             | `(security, funding currency, at, per-unit price)` |
| Dividend (cash)             | None                     | Same-commodity event — no valuation. |
| DRIP / reinvestment         | Buy half's security leg  | Dividend half is same-commodity; buy half annotates the security leg. |
| Transfer (same-currency)    | None                     | Same-commodity. |
| Transfer (cross-currency)   | Source leg               | `(src currency, dst currency, at, implied rate)` — e.g. $100 USD → $135 CAD emits `(USD, CAD, at, 1.35)`. |
| Balance entry               | None                     | Both splits in asserted commodity — per-commodity balance. |

## Price collision rules

**Within a single source at the same `PriceAt`:** rejected at both API and DB layers. The DB constraint enforces it; the API surfaces a Problem Details directing the user to resolve the existing row.

**Across different sources at the same `PriceAt`:** allowed. Two providers that both publish at 16:00 UTC daily close are normal, and a user-typed `Manual` row may coexist with a `Transaction`-derived row or any external-source row at the same instant. The lookup tiebreaker resolves which one wins (see *Lookup and carry-forward*) — `Manual` outranks `Transaction` outranks External, so a manual override always takes precedence over an automated assertion at the same instant.

**Transaction edited to collide with an existing same-source row at the same instant:** rejected. The user resolves by changing the transaction's time or deleting the blocking row.

**Importer fetching a row that already exists for that source:** upsert the `Rate` in place, never duplicate. Providers correcting historical observations is the normal case.

## Transaction-linked price lifecycle

Each linked price row is a materialised view of exactly one split's price annotation (`Split.UnitPriceRate` + `Split.UnitPriceCommodityId`) at the transaction's `OccurredAt`.

- **Transaction create:** for every annotated split, a price row is created with `CreatedByTransactionId` set to the transaction.
- **Transaction edit (`UnitPriceRate` on an existing annotation changed):** the linked price row's `Rate` is updated in place. The row's Id is preserved.
- **Transaction edit (`OccurredAt` / `Split.Commodity` / `Split.UnitPriceCommodityId` changed):** the linked price row's corresponding fields are updated in place, unless the new `(commodity, quote, at)` triple collides with an existing row — then the edit is rejected.
- **Transaction edit (annotation added to a previously-unpriced split):** a new linked price row is created.
- **Transaction edit (annotation removed from a split):** the linked price row is deleted.
- **Transaction delete:** all linked price rows cascade.

## Lookup and carry-forward

To value a holding `(commodity C, quantity Q)` in display currency `D` at instant `T`:

1. **Same-currency case** — if `C == D`, value is `Q`; no price lookup.
2. **Direct lookup** — find the most recent price row with `(C, D, PriceAt ≤ T)` across all enabled sources. If present, value is `Q × rate`.
3. **Inverted direct lookup** — find the most recent price with `(D, C, PriceAt ≤ T)` across all enabled sources. If present, value is `Q × (1 / rate)`.
4. **One-hop chain** — for each currency `X`, resolve `(C, X)` and `(X, D)` independently using steps 2–3 (each leg picks its own most-recent observation across sources). Combine as `rate(C,X) × rate(X,D)`. Inverted hops count. When multiple intermediaries are available, pick the one whose older leg has the most recent `PriceAt`.
5. **No path** — holding is **unpriced**. See *Missing-price UX*.

Chains deeper than two hops are not attempted. In practice USD is the usual intermediary for non-USD display currencies.

**Source identity does not affect ranking** — the lookup is pure freshness across all enabled sources. A `Manual` row from yesterday outranks a `Bank of Canada` row from a week ago not because `Manual` is preferred, but because it is more recent. If a household needs to suppress a source whose data has gone stale or wrong, the lever is to disable (or delete) the source, not to outrank it.

**Tiebreaker — same `PriceAt` from multiple enabled sources.** When two enabled sources have a row at exactly the same `PriceAt` for the same pair, rank by source `Kind` (`Manual` > `Transaction` > External), then by `CommodityPrice.CreatedAt` desc. A user-typed assertion still trumps automation in the rare tie; otherwise the most-recently-inserted row wins. Deterministic; no priority/staleness configuration needed.

**Disabled sources are excluded** from every step above. Their rows remain in storage (so re-enabling restores them without re-import) but are invisible to valuation while disabled.

## Missing-price UX

A holding with no resolvable price at the query instant renders as *unpriced*:

- Native amount shown (e.g., "50.00 VEQT").
- Display-currency value shown as `—` with an *Add price* hint linking to the commodity's Prices tab.
- Net-worth totals exclude unpriced holdings and surface a "*N holdings unpriced*" warning.

Never default an unpriced holding to zero or NaN — silent holes in net worth defeat the purpose of the tracker.

## Price precision

**Storage:** PostgreSQL `numeric` (variable precision). EF maps `decimal` to `numeric` without `HasPrecision` configuration.

**Display:** `max(quoteCurrency.DisplayPrecision, 4)`. CAD balances display at 2 decimals, but a per-share price of VEQT in CAD displays at 4. Prices needing more precision (e.g., BTC pairs) should render at up to `commodity.DisplayPrecision` if higher; the v1 prices tab implements only the `max(quote, 4)` half and renders the high-precision case at 4 decimals — bumping for `commodity.DisplayPrecision` is a follow-up.

## Source management UI

Route: `/finances/settings/price-sources` (under the existing Finances settings area, sibling of any other finance-wide settings).

**List view** shows every registered source — system and external — with: display name · kind · IsEnabled · `LastImportAt` · last-import-status pill · # prices contributed. The status pill is the at-a-glance health indicator (success / failed / never-run / disabled). Display names come from a per-enum-value localisation entry in code, not a stored column.

The list is statically the set of registered enum values — there is no "create source" affordance. New providers come in through code releases.

**Edit** (external sources only): IsEnabled, Config, PairAllowList. Kind is rendered but immutable — it *is* the enum value. The Config form is rendered from a per-provider schema declared by the `IPriceProvider` implementation. The endpoint upserts the corresponding `CommodityPriceSourceSettings` row (creates one on first edit if seeding hasn't yet covered it).

**Config field types.** The schema supports four field types: `String`, `Integer`, `Boolean`, and `Secret`. `Secret` fields (e.g., API keys) are rendered as `type="password"` in the form, masked in the source-detail GET response (`••••` plus the last 4 chars when long enough), and treated as no-op on PATCH when the submitted value is empty or matches the masked form — so an admin saving the form without re-entering the key keeps the existing value.

**System sources** (`Transaction`, `Manual`) appear in the list for visibility — clicking opens a read-only detail page showing their prices and aggregate stats but offering no edit/disable actions. There is no settings row to edit.

**Per-source detail page** (external only) also hosts the source's recurring schedule (a single cron expression, optional) and a *Run now* button that enqueues an ad-hoc import attributed to the admin who clicked it. Schedule edits write through to the existing `processing.schedules` table — the schedule's identity is `(JobType=ImportCommodityPrices, Payload.Source=<enum value>)`, at most one schedule per source. The page is the user-friendly facade over that table for finance-specific schedules. The central admin schedules list (`/admin/schedules`) still shows the same rows as a fallback view.

## Background import jobs

A new `ProcessingJobType.ImportCommodityPrices`. Payload: `{ Source, FromAt?, ToAt?, CommodityIds? }` where `Source` is the enum value. Handler:

1. Loads the source's settings row. If the enum value is a system value (Kind=Transaction or Manual), fails with `IStructuredJobError` ("source is system-managed"). If no settings row exists or `IsEnabled=false`, fails with "source disabled".
2. Resolves the provider strategy by enum value. Each External enum value is registered with exactly one `IPriceProvider` implementation via DI.
3. Computes the target commodity set — see *Import scope*.
4. Fetches observations from the provider for the target commodities over `[FromAt, ToAt]`.
5. Upserts rows under the source enum value, keyed by `(CommodityId, QuoteCurrencyId, PriceAt, Source)`. Existing rows have their `Rate` updated; new rows are inserted. On success, updates the settings row's `LastImportAt = now()`, `LastImportStatus = Success`, `LastImportMessage = null`. On partial success (see below), `LastImportStatus = PartialSuccess`, `LastImportMessage = <provider-supplied warning>`.
6. Audit-logs the upserts via `auditLogger.ForUser(job.CreatedByUserId)`. A scheduled run is system-attributed → no creator → audit entries are anonymous (matching existing system-attributed convention in `processing/overview.md`). A *Run now* run is attributed to the admin who triggered it.

On failure, the handler updates the settings row's `LastImportStatus = Failed` / `LastImportMessage = <message>` before re-throwing — so the source list status pill reflects the latest run regardless of how the job terminated.

**Idempotency.** Re-running an import for an overlapping date window updates rates in place, never duplicates. Providers correcting historical observations is the normal case; the upsert preserves that. The handler is also re-entry safe: a crash mid-run leaves a partial set of upserts already committed, and the next run re-fetches and re-upserts those rows without harm.

**Partial success.** A provider that hits a recoverable limit mid-run (rate-limit throttling, transient single-symbol errors) returns whatever observations it gathered before the limit and surfaces a warning string. The handler commits the partial set as normal, sets `LastImportStatus = PartialSuccess`, and stores the warning in `LastImportMessage`. The next scheduled run re-fetches with `FromAt = settings.LastImportAt` exactly as for a clean Success run — so the unfetched window is naturally retried. The source list distinguishes partial success from clean success in the status pill, but otherwise treats both as the source being healthy enough to keep auto-running.

**Recurring imports** use the existing `processing.schedules` table — at most one schedule per source. The schedule's `Payload` carries the `Source` enum value; `JobType = ImportCommodityPrices`. The admin schedules endpoint's `JobType` allow-list expands to include this type. Per-source schedule UX lives on the source detail page (see *Source management UI*).

**Notifications: always silent in v1.** Both scheduled (system-attributed) and *Run now* (admin-attributed) runs surface only in the admin job list and through the source-list "last import status" pill. No SignalR toast, even for *Run now*. The admin job list carries the full error string for failed runs. Per-user toasts can be added later as an opt-in if it turns out admins miss failures.

**v1 providers.** Bank of Canada Valet (FX, free, no API key). Registers an enum value and an `IPriceProvider` implementation, and declares its supported pairs so import scope can intersect with the household's holdings.

## Provider coverage model

Each `IPriceProvider` declares what it covers via two complementary properties:

- **`SupportedPairs`** — explicit `(symbol, exchange?, quoteCurrency)` tuples. Used by providers with an enumerable universe (e.g., Bank of Canada's fixed currency list).
- **`CoveredSecurityExchanges`** — exchange codes for which the provider claims to price *any* security listed there. Used by providers with an unbounded universe (e.g., AlphaVantage covering every NASDAQ-listed equity).

The handler accepts a held commodity into the import set if `(Symbol, Exchange) ∈ SupportedPairs` **OR** `Kind == Security` AND `Exchange ∈ CoveredSecurityExchanges`. A provider may use both — e.g., a future provider that covers all NASDAQ stocks plus a curated list of OTC names. Both properties default to empty; a provider with both empty would never be invoked.

## Import scope

Each import asks the provider for the **commodities the household actually holds** — the set of commodities referenced by any non-archived account split — intersected with the provider's supported pairs. This keeps the per-run network cost small (one AlphaVantage call per held security, not the entire NASDAQ universe) and avoids accumulating prices for commodities the household will never need.

**Opt-in `PairAllowList`** on the source's settings row lets users track an instrument they don't yet hold (e.g., watching VEQT before buying). The handler unions the held set with `PairAllowList` before calling the provider.

**Securities are fetched in their primary quote currency only** — e.g., VEQT in CAD because it trades on TSX, AAPL in USD because it trades on NASDAQ. Other display currencies resolve via FX chaining at lookup time (a held VEQT viewed in USD chains through `(VEQT, CAD)` × `(CAD, USD)`). The primary quote is **derived from the security's `Exchange` by the provider** — there is no `PrimaryQuoteCurrency` column on `Commodity`. Each provider owns its `Exchange → quote currency` table; an exchange the provider doesn't recognise puts the security outside its coverage and it is silently skipped. If the household holds the same security on different `Exchange` qualifiers (e.g., `AAPL · NASDAQ` vs `AAPL · OTHER`), each is treated as a separate commodity per the existing exchange-qualifier rules, and each gets its own primary quote.

**Exchange-code translation.** Homestead's `Commodity.Exchange` is a free-text user-entered string (see *Exchange qualifier (securities)*). When a provider's external API uses a different code system (suffixes, MIC codes, vendor-specific tickers), the provider owns the translation table from Homestead codes to its own format. Recommended common Homestead codes for clarity: `NASDAQ`, `NYSE`, `NYSEARCA`, `TSX`, `TSXV`, `LSE`, `XETRA`, `JPX`, `HKEX`, `ASX`. Users are not constrained to these — any string works — but a provider that doesn't recognise the user's exchange code will skip that security.

**Backfill window.** First run for a new source backfills from the household's earliest user activity (the existing rule — see the conceptual model). Subsequent scheduled runs use `FromAt = settings.LastImportAt`, `ToAt = now()`. *Run now* lets the admin override `FromAt`/`ToAt` for narrower one-off backfills (e.g., "re-fetch the last 30 days because the provider corrected its data").

This honours the existing rule: *Prices are captured forward from the user's earliest recorded activity. There is no backfill for dates before the user began tracking.*

## Audit

`CommodityPrice` is audit-logged like every other entity — `Created` / `Updated` / `Deleted` entries via `IAuditLogger` on every mutation, regardless of source. Transaction-linked rows get their own audit entries; the `CreatedByTransactionId` FK provides correlation. Imported rows are audit-logged under the importing job's attribution (system-attributed for scheduled, admin-attributed for *Run now*).

`CommodityPriceSourceSettings` mutations (edit, enable/disable) are audit-logged the same way.

## Future-compatibility

- **More providers.** Add an enum value, implement `IPriceProvider`, register it against the new value, optionally declare a config schema for the source-edit form. No schema change.
- **Source priority.** v1 picks the most recent observation across all enabled sources (with a Kind-based tiebreaker). If a household ever wants "trust X over Y even when Y is fresher" — e.g., a paid Bloomberg feed should outrank a free fallback — add a `Priority` column to `CommodityPriceSourceSettings` and adjust the lookup to a priority-then-freshness pick. Additive; no migration of existing rows.
- **Per-pair source overrides.** "BoC for FX, AlphaVantage for stocks" via a `CommodityPriceSourcePairOverride` table keyed by `(Source, CommodityId, QuoteCurrencyId)`. Out of scope for v1 — global enable/disable + per-source `PairAllowList` covers the early use cases.
- **Configurable staleness thresholds.** Per-kind `StaleCurrencyPriceDays` and `StaleSecurityPriceDays` on `FinanceSettings`, used by staleness indicators on the commodity list and source list. Schema-only; UI follows.
- **Per-user import notifications.** v1 is silent (admin job list only). If admins start missing failures, add an opt-in toast/email path keyed off the settings row's `LastImportStatus`.
- **CSV import as a built-in source.** A `CsvUpload` enum value plus a provider impl reading from a designated upload location lets users one-shot historical pricing for instruments no built-in provider covers. If users need multiple distinct CSVs (one per statement, one per broker export), the natural extension is a per-upload-id sub-table whose rows reference `Source = CsvUpload`; that lift is additive when the use case materialises.
- **Inline commodity creation.** v2 adds *+ Create new security* shortcuts inside `Add position`, `Investment buy`, and other security-picking flows.
- **Deeper FX chaining.** If 2-hop coverage proves insufficient, breadth-first search over the pair graph can be implemented without schema changes.
