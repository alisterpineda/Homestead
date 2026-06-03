# Valuation — Reference

Detailed design for how computed balances and positions are exposed to the frontend. Expands on the *Core computed values* summary in [overview.md](overview.md#core-computed-values). Read this before designing, implementing, or reviewing any read-side endpoint that returns an account balance, a position, or any number expressed in the household display currency.

This document scopes only the **API contract for valuations**. The mechanics of how a value is computed (split summation, carry-forward price lookup, FX chaining) live in [commodities.md](commodities.md#lookup-and-carry-forward); the user-facing surfaces that consume these values (account list, account detail, Positions tab) live in [accounts.md](accounts.md#account-list).

## Conceptual model

There are two questions the UI asks about an account:

1. **What is it worth right now?** — answered by the account's current `displayValue`, plus a single `nativeBalance` for single-commodity accounts. Used by every account list, every account-detail header, and the household net-worth summary card.
2. **What does it hold right now?** — answered by the per-commodity position breakdown. Used only by the Positions tab on Investment accounts.

v1 answers (1) inline on the account resource and answers (2) via a dedicated sub-resource. v1 does **not** answer "what was it worth on date D?" outside the net-worth time-series endpoint — see *Out of scope*.

## Hybrid shape

Two read paths are exposed:

- **Inline on `AccountResponse`** — `GET /finances/accounts` and `GET /finances/accounts/{id}` carry the account's "as of now" valuation directly. One round trip serves the dominant page.
- **Sub-resource for positions** — `GET /finances/accounts/{id}/positions` returns the per-commodity breakdown for Investment accounts. Single-position accounts do not need this endpoint; their entire position is already captured by `nativeBalance`.

Household net worth — the total plus the asset / liability breakdown — is delivered by a dedicated net-worth time-series endpoint and surfaced on the Reports page, not rolled up in the frontend from these inline values. The account list itself is a flat list and computes no per-account subtotals, so no parallel summary shape is forced onto the list response.

## `AccountResponse` additions

Three fields are added to the existing `AccountResponse`:

| Field                  | Type            | Meaning                                                                                                                  |
|------------------------|-----------------|--------------------------------------------------------------------------------------------------------------------------|
| `displayValue`         | `decimal?`      | The account's value as of now, expressed in the household display currency. Sign-flipped for the wire? **No — see below.** |
| `nativeBalance`        | `decimal?`      | The account's quantity in its reporting currency (single-position accounts only). `null` for Investment accounts.        |
| `unpricedHoldingCount` | `int`           | Count of held commodities (non-zero quantity) that have no resolvable price as of now. `0` when fully priced or empty.  |

**Sign on the wire.** Both `displayValue` and `nativeBalance` are returned with the **Beancount-style stored sign** — asset accounts positive on increase, liability accounts negative on increase. The UI is responsible for flipping liability signs to "amount owed" at render time. This keeps the API contract uniform and lets the frontend apply its presentation rules (sign-flip, currency formatting, native/display split) in one place. Net worth, computed as `SUM(displayValue)` across all non-system accounts, is correct without per-type arithmetic.

**Display currency.** `displayValue` is always denominated in the household display currency, fetched from `FinanceSettings`. The currency is not echoed on each `AccountResponse` — clients read it from the settings endpoint and apply it uniformly.

### Null vs zero

- `displayValue = 0`: the account is empty (no positions, or all positions sum to zero quantity).
- `displayValue = null`: the account holds non-zero quantities and **none** of them resolve to a price.
- `displayValue = X` (with `unpricedHoldingCount > 0`): the account has both priced and unpriced holdings; `X` is the priced subset's value, and the count flags the gap.

`null` is used deliberately rather than `0` — silently rendering an unpriced holding as zero defeats the purpose of the tracker. Frontend renders `null` as `—` with a hint to add a price (matches the *Missing-price UX* rule in [commodities.md](commodities.md#missing-price-ux)).

`nativeBalance` follows the same rule per single-commodity account, but the denominator (the reporting currency) is by definition resolvable when the account holds it natively, so `nativeBalance` is `null` only for Investment accounts.

## `GET /finances/accounts/{id}/positions`

Returns the per-commodity breakdown for an Investment account, "as of now." Available on Investment accounts only — other kinds 404 here (their state is fully captured by `nativeBalance` on the account resource).

**Response item shape** (one per commodity held with non-zero quantity):

| Field           | Type        | Meaning                                                                                                       |
|-----------------|-------------|---------------------------------------------------------------------------------------------------------------|
| `commodityId`   | `Guid`      | The held commodity.                                                                                           |
| `quantity`      | `decimal`   | Native quantity (the amount in the commodity's own units), signed per the Beancount convention (always positive for assets in v1's transaction shapes). The Positions tab renders the native amount straight from this field. |
| `displayValue`  | `decimal?`  | Position value in the household display currency. `null` when no price resolves for this commodity.            |
| `shareOfAccount`| `decimal?`  | This position's share of the account's *priced* total, as a fraction in `[0, 1]`. `null` when the position itself is unpriced. The priced positions sum to `1.0`; unpriced positions render `—` in the UI. |

Positions sort by `displayValue` descending, with unpriced positions trailing in commodity-symbol order.

The "share of priced total" rule means the percentages always add up to 100% from the user's perspective. The Positions tab surfaces a "*M of N positions unpriced*" warning when applicable — see [accounts.md](accounts.md#account-detail).

## Performance posture

Per [overview.md](overview.md#core-computed-values), all valuations are computed at read time; nothing is cached at the data layer in v1.

- **Account list** (`GET /finances/accounts`) is the hot path. The handler walks each account's splits, collapses them per commodity, looks up the carry-forward price, and sums. At household scale (tens of accounts, hundreds of transactions per year) this is well within request-time budgets.
- **Single-account read** (`GET /finances/accounts/{id}`) is the same work scoped to one account.
- **Positions** (`GET /finances/accounts/{id}/positions`) is the same work but emits per-commodity rows instead of a sum.

If profiling later shows the list endpoint is too slow, options in priority order are: (1) batch the price lookups across all accounts in one query, (2) materialise per-account daily balances behind a background job, (3) cache the household-wide rollup with invalidation on transaction mutation. None of these require a contract change.

## Out of scope for v1

- **No historical-balance endpoint.** The only v1 surface that needs point-in-time per-account valuation is the net-worth chart, which has its own time-series endpoint (specced separately). Endpoints like `GET /finances/balances?asOf=...` or an `?asOf=` parameter on the account / positions endpoints are deliberately not added — they would expand the contract for no v1 caller.
- **No per-account historical positions.** Same reasoning. The Positions tab is "as of now" only per [accounts.md](accounts.md#account-detail).
- **No server-computed rollups on the account read path.** `GET /finances/accounts` carries only per-account inline valuations — no `/finances/summary`-style Kind subtotals or household totals, and the account list does not compute them client-side either. Net worth (total + asset / liability breakdown) is computed server-side by the separate net-worth time-series endpoint and shown on the Reports page; the headline is that series' most-recent point.
- **No per-commodity Equity:Adjustments breakdown.** The Adjustments balance is intended to surface as a single display-currency total (_planned, not yet built in v1_ — see [overview.md](overview.md#distinctive-ux-decisions-worth-preserving)). A per-commodity decomposition is a v2 affordance.

## Future-compatibility

- **`?asOf=` parameter.** Both the account read endpoints and the Positions endpoint can accept an optional `asOf: DateTimeOffset` later without breaking existing callers. The handler already does an "as of now" lookup; parameterising the lookup instant is a localised change.
- **Dedicated `/finances/balances?asOf=` endpoint.** If statement reconciliation or a "balance after this transaction" column on the Activity tab arrives in v2, a bulk historical-balance endpoint can be added without changing the inline shape.
- **Server-side rollups.** A `/finances/summary` endpoint can be introduced if the frontend rollup logic needs to be shared across more pages. The inline valuations remain the source of truth.
- **Background-cached valuations.** A `valuations` table keyed by `(accountId, asOf)` with eventual-consistency invalidation can be added without contract change — handlers continue to expose `displayValue`, just sourced from cache.
