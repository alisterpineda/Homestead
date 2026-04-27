# Commodities & Prices — Reference

Detailed design for commodity and price management in the Finances module. Expands on the summaries in [SKILL.md](../SKILL.md#commodities) and [SKILL.md](../SKILL.md#prices). Read this before designing, implementing, or reviewing any commodity- or price-related functionality — commodity creation/editing, the ISO 4217 currency seed, the exchange qualifier, price sources, carry-forward rules, FX chaining, or the commodity/price UI.

## Conceptual model

A **commodity** is anything that has a quantity and can be held in an account — a currency (CAD, USD, GBP, ...) or a security (VEQT, AAPL, ...). Currencies and securities live in one table; only behaviour in certain transaction shapes differs.

A **price** is a single observation of a commodity's value expressed in a currency at a specific instant: the tuple `(commodity, quote currency, priceAt, rate)`, read as *"1 unit of `commodity` was worth `rate` units of `quote currency` at `priceAt`."* Prices are point observations, not intervals. Missing points resolve via carry-forward (see *Lookup and carry-forward*).

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

Single modal opened from `/finances/commodities` (Securities tab). Kind is always `Security` — currencies are never user-created. Fields:

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

Two tabs: **Securities** | **Currencies**. Currencies drown out the few securities a household tracks, so they're separated.

**Securities tab:**
- Columns: Symbol · Exchange · Name · Display precision · # prices · latest-price staleness indicator.
- Row actions: Edit, Delete (when unreferenced).
- Header action: *New Security*.
- Search: matches Symbol or Name. Exchange displayed but not a primary search target.

**Currencies tab:**
- Columns: Symbol · Name · Display precision.
- Read-only — no row actions.
- Search: matches Symbol or Name.

The list endpoint projects the per-row aggregates the table needs — `PriceCount`, `LatestPriceAt`, and `CanDelete` — onto `CommodityResponse` so the table renders in a single round-trip. `CanDelete` mirrors the same reference predicate `DeleteCommodityHandler` enforces (account reporting currency, splits, split price annotations, price rows on either side); the row's Delete affordance hides when it's false. Seeded currencies always have `CanDelete=false`.

## Commodity detail

Route: `/finances/commodities/:id`.

**Header:** breadcrumb · symbol · exchange badge (securities only) · name · kind.

**Tabs:**

- **Info** — metadata: symbol, exchange (securities only), name, kind, display precision, system-managed badge.
- **Prices** — chronological list of price observations with source labels. Section header above the table carries the *New manual price* action (rendered with the table body, not in the page-header strip, so the header stays stable as the user toggles tabs).

Seeded currencies render read-only throughout.

## Prices — model

```
CommodityPrice {
  Id: Guid
  CommodityId: Guid              -> Commodity
  QuoteCurrencyId: Guid          -> Commodity (must be Kind=Currency)
  PriceAt: DateTimeOffset        (stored UTC; uniqueness axis)
  Rate: numeric                  (> 0)
  CreatedByTransactionId: Guid?  -> AccountTransaction (null = manual)
  audit fields
}
```

**Uniqueness:** `UNIQUE(CommodityId, QuoteCurrencyId, PriceAt)` — enforced by the database.

**Rate validation:** `Rate > 0` — enforced at the API layer and by database constraint.

**Self-prices:** rejected. A row with `CommodityId == QuoteCurrencyId` is meaningless and fails validation.

**Quote must be a currency:** `QuoteCurrency.Kind == Currency`. Securities can be quoted in any currency, but only currencies can serve as the quote side.

## Price time semantics

`PriceAt` is a `DateTimeOffset`, stored UTC. This gives well-defined ordering for same-day observations, alignment with transaction timestamps (see *Transaction time semantics* in SKILL.md), and a single axis for carry-forward lookups.

Transaction-linked prices inherit the source transaction's `OccurredAt`. Manual entry collects a date with an optional time; the frontend defaults the time to `00:00 UTC` on the entered date when the user only picks a date. The API accepts a raw `PriceAt: DateTimeOffset` — the server does not default or normalise the time. On a day that has both a 00:00-UTC manual price and a 12:00-UTC transaction-linked price, carry-forward at instants ≥ 12:00 UTC picks the transaction-linked row (most recent `PriceAt ≤ T`).

`PriceAt` may be up to **one day ahead** of current UTC — the server rejects timestamps further in the future, which avoids accidentally staging prices for arbitrary dates.

## Price sources

Each price is either **manual** or **transaction-linked**:

- **Manual:** `CreatedByTransactionId` is null. Created via *New manual price* on the commodity detail page, or (future) batch imports.
- **Transaction-linked:** `CreatedByTransactionId` points to the transaction that created the row. The row is materialised from a **split price annotation** (`Split.UnitPriceRate` + `Split.UnitPriceCommodityId`) on the source transaction — every annotated split emits exactly one `CommodityPrice` row. Editing the annotation updates the row in place; deleting the transaction deletes the row.

Transaction-linked prices are **read-only** in the prices UI — the row is labelled *Transaction* and has no edit or delete affordance. The label carries a tooltip explaining that the price is edited via its source transaction. (A clickable deep-link to the originating transaction is deferred to a later pass — there is no per-transaction route today, and resolving the price's account context for navigation needs an extra fetch the v1 prices tab does not perform.) To change a transaction-linked price the user edits the transaction.

Manual prices: only **Rate** is editable after creation. Changing Commodity, Quote Currency, or `PriceAt` is conceptually a new observation — delete and re-add.

**Security** opening balances on an Investment account (via Add Position) *may* carry a price annotation on the security split at `OccurredAt` = asOfDate @ 12:00 UTC — the per-unit price is **optional**. If supplied, the security leg is annotated `@ price reportingCurrency` and the annotation materialises as a linked price row; the transaction balances per-currency via the annotation (asset +qty VEQT @ price ↔ equity -price×qty CAD). If omitted, the position is captured unpriced: both splits are in the security commodity, the transaction balances per-commodity (asset +qty VEQT ↔ equity -qty VEQT), no price row is emitted, and the holding renders via carry-forward at net-worth time (or as *unpriced* if no price ever resolves). This lets users assert "I had 100 VEQT on this date" without forcing them to pin down a historical per-share price; pricing is a separable concern. Cash opening balances never emit a price row (both splits same currency — per-commodity balance, no annotation). When a security opening balance is later updated, the annotation moves with it (rate in place, or `PriceAt` on date change — rejecting if another row already occupies the target instant); an update that first supplies a price to a previously-unpriced opening balance adds an annotation to the security leg and creates a new linked row (the opposite-side split's commodity flips from the security to the reporting currency as a consequence). Deleting the opening balance cascades the linked row via FK.

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
| Stock split                 | None (forbidden)         | Quantity-only adjustment. Annotation rejected. |
| Transfer (same-currency)    | None                     | Same-commodity. |
| Transfer (cross-currency)   | Source leg               | `(src currency, dst currency, at, implied rate)` — e.g. $100 USD → $135 CAD emits `(USD, CAD, at, 1.35)`. |
| Balance entry               | None                     | Both splits in asserted commodity — per-commodity balance. |

## Price collision rules

**Manual price colliding with a transaction-created price at the same `PriceAt`:** rejected at both the API layer (returning a Problem Details response directing the user to resolve the existing row first) and the database layer (via the uniqueness constraint). Applies in both directions — a manual write is rejected when a transaction-linked row already exists at that instant, and a transaction write is rejected when a manual row exists.

**Transaction edited to collide with another price row:** same rejection. The user resolves by changing the transaction's time or deleting the blocking row.

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
2. **Direct lookup** — find the most recent price row with `(C, D, PriceAt ≤ T)`. If present, value is `Q × rate`.
3. **Inverted direct lookup** — find the most recent price with `(D, C, PriceAt ≤ T)`. If present, value is `Q × (1 / rate)`.
4. **One-hop chain** — for each currency `X`, find `(C, X)` and `(X, D)` both ≤ `T`. Combine as `rate(C,X) × rate(X,D)`. Inverted hops count. When multiple intermediaries are available, pick the one whose older leg has the most recent `PriceAt`.
5. **No path** — holding is **unpriced**. See *Missing-price UX*.

Chains deeper than two hops are not attempted. In practice USD is the usual intermediary for non-USD display currencies.

## Missing-price UX

A holding with no resolvable price at the query instant renders as *unpriced*:

- Native amount shown (e.g., "50.00 VEQT").
- Display-currency value shown as `—` with an *Add price* hint linking to the commodity's Prices tab.
- Net-worth totals exclude unpriced holdings and surface a "*N holdings unpriced*" warning.

Never default an unpriced holding to zero or NaN — silent holes in net worth defeat the purpose of the tracker.

## Price precision

**Storage:** PostgreSQL `numeric` (variable precision). EF maps `decimal` to `numeric` without `HasPrecision` configuration.

**Display:** `max(quoteCurrency.DisplayPrecision, 4)`. CAD balances display at 2 decimals, but a per-share price of VEQT in CAD displays at 4. Prices needing more precision (e.g., BTC pairs) should render at up to `commodity.DisplayPrecision` if higher; the v1 prices tab implements only the `max(quote, 4)` half and renders the high-precision case at 4 decimals — bumping for `commodity.DisplayPrecision` is a follow-up.

## Audit

`CommodityPrice` is audit-logged like every other entity — `Created` / `Updated` / `Deleted` entries via `IAuditLogger` on every mutation, regardless of source. Transaction-linked rows get their own audit entries; the `CreatedByTransactionId` FK provides correlation.

## Future-compatibility

- **External imports.** The data model accommodates bulk price ingestion (CSV, broker APIs, exchange-rate providers). Importers emit manual price rows at scale; no schema change required. Deduplication uses the `(commodity, quote, at)` uniqueness constraint.
- **Configurable staleness thresholds.** v2 adds per-kind `StaleCurrencyPriceDays` and `StaleSecurityPriceDays` to `FinanceSettings`. The UI reads these when rendering staleness indicators.
- **Inline commodity creation.** v2 adds *+ Create new security* shortcuts inside `Add position`, `Investment buy`, and other security-picking flows.
- **Deeper FX chaining.** If 2-hop coverage proves insufficient, breadth-first search over the pair graph can be implemented without schema changes.
