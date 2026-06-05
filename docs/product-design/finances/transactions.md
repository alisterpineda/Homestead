## Transactions & Splits — Reference

Detailed design for transaction and split management in the Finances module. Expands on the summaries in [overview.md](overview.md#transactions), [overview.md](overview.md#splits), and [overview.md](overview.md#transaction-shapes-user-facing). Read this before designing, implementing, or reviewing any transaction-shape, balance-entry, edit/delete, validation, or reconciliation work.

Opening-balance creation, editing, and deletion live in [accounts.md](accounts.md#initial-balance-mechanics) — this document treats opening balances only where they intersect with general edit/delete rules. Transaction-linked price behaviour (lifecycle, collisions, carry-forward) lives in [commodities.md](commodities.md#prices--model) — this document only summarises which shapes create price rows.

## Conceptual model

A **transaction** is one event at one point in time. It carries an `OccurredAt` timestamp (`DateTimeOffset`, stored UTC), a description, and one or more splits. A **split** is the per-account leg: account, commodity, quantity in the commodity's native units, and an optional per-unit price annotation (`UnitPrice` rate + `UnitPriceCommodityId`) that bridges this leg into another commodity for balance purposes.

The data-layer invariant is **per-commodity weight balance**: for every commodity `C` appearing as either a split's commodity (where the split is unpriced) or as a split's price-annotation commodity, the sum of split **weights** in `C` is zero. A split's weight is:

- `Quantity` in `Split.Commodity` if the split has no price annotation, or
- `Quantity × UnitPrice.Rate` in `UnitPrice.Commodity` if it does.

Each split contributes one weight, to exactly one commodity. Same-commodity transactions (transfer within a currency, dividend, balance entry on any commodity) balance trivially per-commodity because every split's weight is in the same commodity and they net to zero. Cross-commodity transactions (cross-currency transfer, buy, sell, DRIP's buy half, optionally-priced security opening balance) rely on at least one annotation to bridge the commodities so both sides resolve to weights in the same quote currency.

The invariant has no exceptions in v1. Quantity-changing events that don't naturally balance (corporate actions like stock splits) are out of scope for v1 and, when added, will be modelled per-commodity rather than as transactions — see [overview.md](overview.md#future-compatibility).

Users never create "transactions" directly. They pick a **shape** — Transfer, Investment Buy, Dividend, ... — which the server expands into the underlying balanced transaction. Users never see the words "split", "annotation", "debit", or "credit" in v1. The shape catalogue is closed; new shapes require a deliberate change.

## Storage shapes

Transactions use TPH (table-per-hierarchy) on `AccountTransaction.Type`. Most shapes share `NormalTransaction`. Two shapes get their own subtype: balance entries (`BalanceEntryTransaction`), because they extend the base contract with an asserted-quantity field; and opening balances (`OpeningBalanceTransaction`), which adds no fields but keeps a stable discriminator so the dedicated opening-balance editors and delete/re-reconcile logic can target opening-balance rows directly.

Transfer, Investment buy, Investment sell, and Dividend are all 2-split `NormalTransaction`s, so split structure alone can't tell them apart. A second discriminator column — `NormalTransaction.NormalShape` (`Transfer | InvestmentBuy | InvestmentSell | Dividend`, stored as a string; NULL on non-normal TPH rows) — records the user-facing shape directly. The mapping reads it back O(1) with no commodity-kind joins, it's unambiguous, and it survives edits, mirroring the `Type`-discriminator philosophy.

| Shape                 | Stored type              | Splits | Priced legs | Notes |
|-----------------------|--------------------------|--------|-------------|-------|
| Opening balance (cash or FX cash in investment) | `OpeningBalanceTransaction` | 2 | 0 | Both splits in same commodity — per-commodity balance. Counterpart = `Equity:Opening Balances`. See [accounts.md](accounts.md#initial-balance-mechanics). |
| Opening balance (security) | `OpeningBalanceTransaction` | 2 | 0 or 1 | Priced only when the user supplies a per-unit price at Add-position time. Unpriced positions balance per-commodity (both splits in the security). |
| Transfer (same-currency) | `NormalTransaction`   | 2 | 0 | Both splits same commodity. |
| Transfer (cross-currency)| `NormalTransaction`   | 2 | 1 | Annotation on the source leg. |
| Investment buy        | `NormalTransaction`      | 2 | 1 | Annotation on the security leg. Both splits on the investment account. |
| Investment sell       | `NormalTransaction`      | 2 | 1 | Mirror of buy. |
| Dividend (cash)       | `NormalTransaction`      | 2 | 0 | Both splits in the dividend currency. Counterpart = system `Income` account. |
| DRIP / reinvestment   | `NormalTransaction` × 2  | 2 each | 0 + 1 | Dividend half unpriced; Buy half priced. Paired via `RelatedTransactionId`. |
| Balance entry         | `BalanceEntryTransaction`| 2 | 0 | Both splits in the asserted commodity. Carries `AssertedCommodityId` + `AssertedQuantity`; counterpart = `Equity:Adjustments`. |

`AccountTransaction.RelatedTransactionId` is a nullable self-FK used in v1 only for DRIP pairing. `OnDelete(SetNull)` — handler logic decides whether the surviving half is kept or removed when its partner is deleted.

### Split schema additions

`AccountTransactionSplit` carries:

- `AccountId` (FK)
- `CommodityId` (FK)
- `Quantity` (numeric, signed)
- `UnitPriceRate` (numeric, nullable, > 0 when set)
- `UnitPriceCommodityId` (FK, nullable, must be `Kind=Currency` when set)
- `Memo` (≤1024, nullable — see *Split memo*)

`UnitPriceRate` and `UnitPriceCommodityId` are either both null (unpriced) or both populated (priced). A priced leg's annotation commodity must differ from `Split.Commodity` — a self-annotation is meaningless and rejected. The annotation's commodity must be a currency (`Kind=Currency`); securities cannot serve as quote commodities, mirroring the rule on `CommodityPrice.QuoteCurrencyId`.

No stored `Value` column. Display-currency values are computed at read time from `(Quantity, price-at-date-in-display-currency)`.

## System accounts used as counterparts

Four hidden system accounts exist per household (see [accounts.md](accounts.md#system-accounts) for `Equity:Opening Balances` and `Equity:Adjustments`). Two are added in v1 specifically to support the transaction shapes here:

- **`Income`** — `AccountType=Income`, multi-position, hidden, system. Counterpart for cash dividends in v1. v2 introduces user-creatable categorised income accounts; this account is renamed to a v2-appropriate "uncategorised" bucket as part of that migration — no historical data is rewritten.
- **`Expense`** — `AccountType=Expense`, multi-position, hidden, system. Reserved for symmetry; no v1 shape posts to it. Seeded so v2 doesn't have to backfill it.

Both are seeded by `SystemAccountSeeder` alongside the equity accounts and never appear in user-facing lists or shape pickers.

## Sign convention

Split **quantities** follow the Beancount convention. Extending the table in [accounts.md](accounts.md#sign-convention-beancount-style) for the new account types:

| Account type | Sign when balance increases |
|--------------|-----------------------------|
| Asset        | Positive                    |
| Liability    | Negative                    |
| Equity       | Negative                    |
| Income       | Negative                    |
| Expense      | Positive                    |

For each commodity, the sum of split weights is zero. Net worth remains a flat sum of asset + liability balances; income/expense balances are not part of net worth in v1.

The UI accepts positive numbers everywhere. The server applies sign flips when writing splits whose account type calls for it. Liability balances render as positive "amount owed."

## Time semantics

All timestamps stored UTC. Defaults per shape:

| Shape                                    | Default time on chosen date | User-overridable? |
|------------------------------------------|-----------------------------|-------------------|
| Opening balance (non-Investment)         | `00:00 UTC`                 | No                |
| Opening balance (Investment, Add position) | `12:00 UTC`               | No (v1)           |
| Transfer (any)                           | `00:00 UTC`                 | No                |
| Investment buy / sell / dividend / DRIP  | `12:00 UTC`                 | Yes               |
| Balance entry                            | `23:59:59 UTC`              | No                |

Investment buy, sell, dividend, and DRIP writes now expose a user-overridable time: a `12:00 UTC` default on a date-only entry, with a collapsible "Add time" affordance for a precise wall-clock instant (see *Editing transactions*). **Add position** (opening balance on an Investment account) still stores the fixed `12:00 UTC` default and exposes no time input in v1. Investment-shape time inputs are entered in the user's browser timezone and converted to UTC server-side: a date-only entry pins to noon UTC, while an expanded time is interpreted as the browser's local wall clock. The server accepts a `DateTimeOffset` and stores it as UTC. The frontend is responsible for converting browser-local times before sending.

`OccurredAt` may be up to **one day past current UTC** to tolerate timezone drift between client and server (Homestead supports cross-timezone households). Further-future timestamps are rejected.

**Same-instant ordering.** When multiple transactions share an `OccurredAt`, the secondary sort key is `(CreatedAt, Id)`. This is rarely user-visible in v1 (display lists already group by date) but matters for any future per-transaction running-balance computation, so the order is committed now.

## Transaction shapes

All examples use the format `Quantity [@ UnitPrice UnitPriceCommodity]`. Unpriced legs omit the `@` clause. System counterpart accounts are written as `<Income>`, `<Equity:Adjustments>`, etc. — these are the hidden system accounts.

### Opening balance

See [accounts.md](accounts.md#initial-balance-mechanics) for the full mechanics, edit/delete rules, and worked examples.

### Transfer

Movement between two of the household's own accounts. UI collects: source account, destination account, source amount, optional destination amount (cross-currency only — server infers single-currency case from matching reporting currencies), date, optional description.

**Same-currency** ($200 CAD, chequing → savings, 2026-04-23):

```
Transaction: Type=Normal, Description="Transfer to Savings", OccurredAt=2026-04-23T00:00:00Z
  Split 1: Account=<chequing>, Commodity=CAD, Quantity=-200
  Split 2: Account=<savings>,  Commodity=CAD, Quantity=+200
```

Weight balance: CAD = -200 + +200 = 0 ✓. No price annotation, no `CommodityPrice` row.

**Cross-currency** ($100 USD → $135 CAD, 2026-04-23):

```
Transaction: Type=Normal, Description="Transfer to CAD chequing", OccurredAt=2026-04-23T00:00:00Z
  Split 1: Account=<USD chequing>, Commodity=USD, Quantity=-100 @ 1.35 CAD
  Split 2: Account=<CAD chequing>, Commodity=CAD, Quantity=+135
```

Weight balance: CAD = (-100 × 1.35) + (+135) = 0 ✓. The source leg's annotation resolves into CAD; the destination leg is unpriced (native CAD). Emits `CommodityPrice (USD, CAD, 2026-04-23T00:00:00Z, 1.35)` — read as "1 USD = 1.35 CAD."

Source and destination quantities are always `-amount` and `+amount` respectively, regardless of account type — no per-account-type sign flip is applied in transfer construction. The Beancount sign convention is satisfied automatically by the flow direction: for a liability destination (paying down a card), `+amount` stores as "less negative" (less owed); for a liability source (cash advance), `-amount` stores as "more negative" (more owed). This differs from opening balances and balance entries, where the user supplies a single positive amount and the server applies the sign flip per the convention table.

**Validation:** source ≠ destination; both writable (not archived, not system); both account commodities valid for their kinds; source amount > 0 and (cross-currency) destination amount > 0; derived rate > 0.

### Investment buy

Purchase of a security funded from a cash position **inside the same investment account**. To buy from external cash, the user does a Transfer first, then a Buy. UI collects: a Buy/Sell side toggle (disabled when editing — side is immutable), investment account (read-only), security, quantity, per-unit price, derived total (read-only, formatted in the funding currency), funding currency, date, optional time, optional memo. The funding-currency picker is **smart-revealed**: it defaults to the investment account's reporting currency and stays hidden in create mode unless the account holds cash in a currency other than its reporting currency — and is always shown when editing, or whenever the selected funding currency differs from reporting.

Example (Buy 2 VEQT @ 50.00 CAD on 2026-04-23):

```
Transaction: Type=Normal, Description="Buy 2 VEQT @ 50.00", OccurredAt=2026-04-23T12:00:00Z
  Split 1: Account=<investment>, Commodity=CAD,  Quantity=-100
  Split 2: Account=<investment>, Commodity=VEQT, Quantity=+2 @ 50.00 CAD
```

Weight balance: CAD = (-100) + (+2 × 50) = 0 ✓. Positions update to +2 VEQT / -100 CAD in the investment account. Emits `CommodityPrice (VEQT, CAD, 2026-04-23T12:00:00Z, 50.00)`.

**Validation:** account is an Investment; security commodity is `Kind=Security`; funding commodity is `Kind=Currency`; quantity > 0; per-unit price > 0; both splits reference the same account.

### Investment sell

Mirror of Buy. Quantities and signs flip; emits a price row identically.

### Dividend (cash)

Cash dividend received into an investment account. UI collects: investment account (shown read-only), payment commodity (currency the dividend was paid in — always shown, unlike Buy/Sell's funding picker which is revealed only when needed), amount, date, optional time, optional description (defaults to "Dividend"). On edit the account cannot be changed; payment currency and amount remain editable.

Example ($5 USD dividend from VFV on 2026-04-23):

```
Transaction: Type=Normal, Description="Dividend from VFV", OccurredAt=2026-04-23T12:00:00Z
  Split 1: Account=<investment>, Commodity=USD, Quantity=+5
  Split 2: Account=<Income>,     Commodity=USD, Quantity=-5
```

Weight balance: USD = +5 + (-5) = 0 ✓. The Income split is negative because income increases (Beancount-style — see *Sign convention*). No annotation, no price row (cash event only).

**Validation:** account is an Investment; payment commodity is `Kind=Currency`; amount > 0.

### DRIP / reinvestment

A dividend whose cash is immediately reinvested in additional units of the paying security (typically fractional). Modelled as **two linked `NormalTransaction`s** at the same `OccurredAt`, paired via `RelatedTransactionId`. This matches how brokerages report DRIP activity (cash dividend line + immediate buy line) and keeps the underlying shapes ordinary.

UI collects: investment account, security, dividend amount (cash), units received (typically fractional), date, optional time. The per-unit reinvestment price is derived as `dividend / units`.

Example ($5 USD dividend from VFV reinvested as 0.05 units @ 100 USD/unit, 2026-04-23):

```
Transaction A: Type=Normal, Description="Dividend from VFV", OccurredAt=2026-04-23T12:00:00Z,
               RelatedTransactionId=B
  Split A1: Account=<investment>, Commodity=USD, Quantity=+5
  Split A2: Account=<Income>,     Commodity=USD, Quantity=-5

Transaction B: Type=Normal, Description="Reinvest VFV dividend", OccurredAt=2026-04-23T12:00:00Z,
               RelatedTransactionId=A
  Split B1: Account=<investment>, Commodity=USD, Quantity=-5
  Split B2: Account=<investment>, Commodity=VFV, Quantity=+0.05 @ 100 USD
```

Each half balances independently. A: USD = +5 − 5 = 0. B: USD = -5 + (0.05 × 100) = 0. The Buy half's annotation emits `CommodityPrice (VFV, USD, 2026-04-23T12:00:00Z, 100)`.

**Editing:** the DRIP editor surfaces the pair as a single logical event. Edits to amount/units/date apply to both halves atomically.

**Deleting:** deleting either half prompts the user to delete both. If they decline, the survivor's `RelatedTransactionId` is cleared and it remains a valid standalone Dividend or Buy.

**Validation:** as for Dividend + Investment buy combined; both halves must reference the same investment account and security; the cash legs in halves A and B must net to zero in the dividend commodity.

### Balance entry

User assertion: *"the balance of this account, in this commodity, on this date, was this much."* The server computes the delta vs. the prior running balance and books it against `Equity:Adjustments` in the asserted commodity.

UI collects: account, commodity (defaults to reporting currency for non-Investment kinds; required pick for Investments — currency or security), asserted amount (positive for liabilities, server flips), date, optional note.

Example (Cash account asserts $1500 CAD on 2026-04-30, prior running balance $1450 → delta +$50):

```
Transaction: Type=BalanceEntry, Description="Balance assertion", OccurredAt=2026-04-30T23:59:59Z
  AssertedCommodityId=CAD, AssertedQuantity=+1500
  Split 1: Account=<cash>,                Commodity=CAD, Quantity=+50
  Split 2: Account=<Equity:Adjustments>,  Commodity=CAD, Quantity=-50
```

Weight balance: CAD = +50 − 50 = 0 ✓.

Example (Credit card asserts $500 owed on 2026-04-30, prior running balance -$450 → delta -$50):

```
Transaction: Type=BalanceEntry, Description="Balance assertion", OccurredAt=2026-04-30T23:59:59Z
  AssertedCommodityId=CAD, AssertedQuantity=-500
  Split 1: Account=<credit card>,         Commodity=CAD, Quantity=-50
  Split 2: Account=<Equity:Adjustments>,  Commodity=CAD, Quantity=+50
```

Example (Investment account asserts 100 VEQT held on 2026-04-30, prior running 95 → delta +5 VEQT):

```
Transaction: Type=BalanceEntry, Description="Balance assertion", OccurredAt=2026-04-30T23:59:59Z
  AssertedCommodityId=VEQT, AssertedQuantity=+100
  Split 1: Account=<investment>,          Commodity=VEQT, Quantity=+5
  Split 2: Account=<Equity:Adjustments>,  Commodity=VEQT, Quantity=-5
```

Weight balance: VEQT = +5 − 5 = 0 ✓. The asserted commodity is a security and the transaction still balances per-commodity with no price annotation needed — the Beancount model handles security balance entries as ordinary same-commodity balances.

`AssertedQuantity` is stored on the transaction itself (not just derivable from the splits) so the user's source-of-truth assertion survives any later edits to earlier transactions. Splits encode the **delta**; `AssertedQuantity` encodes the **target**; reconciliation derives splits from `(target − prior running)`.

**Uniqueness:** one balance entry per `(account, asserted commodity, date)`. Enforced by a partial unique index on `(AccountId, AssertedCommodityId, OccurredAt)` filtered to `Type=BalanceEntry`. The 23:59:59 UTC time-of-day default makes per-instant uniqueness equivalent to per-day uniqueness.

**Multi-position investment accounts:** one balance entry per commodity per date. A user reconciling an Investment account from a monthly statement enters N balance entries (one per security + one per cash currency).

**Validation:** account writable; asserted commodity is held (or zero-held — first-time assertion of a fresh position is allowed); asserted quantity is finite; sign matches the user's positive-input contract per account type; both splits unpriced and in the asserted commodity.

## Balance-entry reconciliation

The reconciliation pass that computes `delta = asserted − prior running` runs **synchronously** on every mutation that touches `(account, commodity)` at or before the affected balance entry's `OccurredAt`. The events that trigger a re-pass:

1. Creating, editing, or deleting any transaction whose splits touch `(account, commodity)`.
2. Creating, editing, or deleting a balance entry on `(account, commodity)` (a balance entry can sit between two others and shift the chain).

For each affected balance entry, the handler recomputes the delta from current splits and rewrites the two splits in place (preserving split `Id`s for audit continuity). `AssertedQuantity` is never rewritten — only the reconciliation splits are.

Prior-running is computed from **quantity** in the asserted commodity — no display-currency conversion at reconciliation time. A CAD balance entry reconciles in CAD; a VEQT balance entry reconciles in VEQT. This keeps reconciliation independent of price movement: a forgotten price update doesn't silently warp past reconciliations.

v1 households are tiny; the full re-pass per affected `(account, commodity)` chain runs inline within the same `SaveChangesAsync`. If chains ever grow large, a deferred/batched pass becomes a future optimisation without changing the data model.

## Shape availability per account kind

The "New Transaction" dropdown on each account's detail page surfaces only shapes that make sense for the account's `Kind`. This is a UI rule — the data layer accepts any valid balanced transaction.

| Kind            | Transfer | Buy / Sell | Dividend | DRIP | Balance entry |
|-----------------|----------|------------|----------|------|---------------|
| Cash            | ✓        |            |          |      | ✓             |
| Credit Card     | ✓        |            |          |      | ✓             |
| Loan            | ✓        |            |          |      | ✓             |
| Other Liability | ✓        |            |          |      | ✓             |
| Other Asset     | ✓        |            |          |      | ✓             |
| Investment      | ✓        | ✓          | ✓        | ✓    | ✓             |

The household-level "+ New Transaction" entry point (when added) offers the union of all shapes and asks the user to pick a target account first.

## Editing transactions

Each transaction shape edits through its **own dedicated editor**, not a single generic one:

- **Transfer** — `PatchTransfer`, via `transfer-sheet`.
- **Balance entry** — `PatchBalanceEntry`, via `balance-entry-sheet`.
- **Opening balance** — `PatchOpeningBalance`, via the per-account (non-Investment) / per-position (Investment) sheets — see [accounts.md](accounts.md#editing-opening-balances).
- **Investment buy / sell** — `PatchTrade`, via `trade-sheet` (one sheet, Buy/Sell toggle disabled in edit).
- **Dividend** — `PatchDividend`, via `dividend-sheet`.
- **DRIP / reinvested dividend** — `PatchDrip` (rewrites both halves atomically) / `DeleteDrip` (both-halves), via `drip-sheet`. Either clicked half routes here (detected by `relatedTransactionId != null`); the sheet seeds from the self-sufficient Buy half. Account, security, and payment currency are locked in edit (delete + recreate to change them). Delete offers a 3-way prompt — both / this one only / cancel.

The date/time of these investment shapes is set via a native date picker plus a collapsible "Add time" affordance: a date-only entry pins the instant to noon UTC, and expanding the time field records a precise wall-clock instant (useful for multiple same-day trades of one security). Whether the per-shape editors later consolidate into a unified one is an open decision, deferred until enough shapes share edit behaviour to justify it. Only the **reads** (`GetTransaction` / `GetTransactions`) and **delete** (`DeleteTransaction`) are shape-agnostic. Every per-shape editor honours the same rules:

- **Shape (`TransactionType` and the underlying construction)** is immutable. A Transfer cannot become a Buy mid-edit. Convert by deleting and re-creating.
- **All other fields are editable**: date/time, description, accounts, commodities, quantities/amounts, per-unit price (where applicable). The server re-validates the resulting splits against the shape's contract and the per-commodity balance invariant.
- **Linked price rows move with the annotation**: rate edits on an annotated split update its linked price row in place; date or commodity edits move the linked row, rejecting on collision with another `(commodity, quote, at)` triple. See [commodities.md](commodities.md#transaction-linked-price-lifecycle).
- **DRIP pair edits** apply to both halves atomically (see *DRIP*).
- **Opening-balance protection**: when an OB's own date is moved, the edit is rejected if it would leave the OB later than any other activity for the same `(account, commodity)` — the OB must remain earliest (enforced in `OpeningBalanceService`). **TODO / known gap:** the symmetric direction — rejecting edits that move a *non-OB* transaction earlier than an existing OB — is part of this design but is **not currently enforced** by the per-shape editors; whether to implement the guard or drop the requirement is an open decision.
- **Balance-entry re-reconciliation** fires for any balance entry on `(account, commodity)` at or after the edited transaction's effective date (see *Balance-entry reconciliation*).
- **Audit**: every edit is field-level audited via `IAuditLogger.Updated(...)` against an `AccountTransactionSnapshot`. Split changes (including annotation changes) are captured as part of the transaction's snapshot diff.

## Deleting transactions

- **Splits** cascade via FK (`OnDelete(Cascade)`).
- **Linked price rows** cascade via FK (`OnDelete(Cascade)` on `CommodityPrice.CreatedByTransactionId`). The handler emits an explicit `audit.Deleted("CommodityPrice", ...)` so the FK cascade isn't silent in the audit log (matching the pattern in `OpeningBalanceService.DeleteAsync`).
- **Related transactions** (DRIP) — see *DRIP*.
- **Opening balances** — delete is **always allowed** (no guard against other transactions referencing the `(account, commodity)` pair). Deletion retroactively shifts the running balance by the removed quantity and triggers re-reconciliation of any downstream balance entries on that pair, with the difference absorbed into `Equity:Adjustments` — see [accounts.md](accounts.md#editing-opening-balances). `OpeningBalanceService.DeleteAsync` returns the touched `(account, commodity)` pairs so the caller re-reconciles.
- **Balance-entry re-reconciliation** fires for any balance entry on `(account, commodity)` at or after the deleted transaction's `OccurredAt`.
- **Audit**: `IAuditLogger.Deleted(...)` for the transaction itself.

## Validation summary

Cross-shape constraints, beyond the per-shape rules above:

- Every split's account is writable: `IsArchived=false`, and not a system account except for the specific counterparts noted (`Equity:Opening Balances`, `Equity:Adjustments`, `Income`, `Expense`).
- Every split's commodity exists.
- Quantities and rates are finite decimals.
- A split's price annotation is either fully populated (`UnitPriceRate` > 0 and `UnitPriceCommodityId` set to a `Kind=Currency` commodity distinct from `Split.Commodity`) or fully null.
- For every transaction, the **per-commodity weight-balance** invariant holds exactly: for each commodity `C`, `SUM(weight in C) = 0` (no rounding tolerance — quantities and rates store as `numeric`).
- For balance entries, exactly two splits, both unpriced and in the asserted commodity, one referencing the asserted account, the other referencing `Equity:Adjustments`.
- For DRIP pairs, both halves persisted in the same `SaveChangesAsync` (no orphan creation).

## Listing transactions

Two surfaces:

- **Account detail → Activity tab** — already specified in [accounts.md](accounts.md#account-detail). Per-account chronological list of every transaction touching that account. Trades are a special case: both legs (cash and security) sit on the same investment account, so the row disambiguates them by annotation — the cash leg is the unpriced split, the security leg is the priced one. The signed row amount comes from the cash leg (Buy negative, Sell positive) formatted in the funding currency, and the subtitle reads `<abs quantity> @ <unit price>` (quantity in the security, price in the funding currency). Buy / Sell / Dividend rows use the "Buy" / "Sell" / "Dividend" labels with trending-up / trending-down / banknote icons.
- **`/finances/transactions`** — household-wide filterable list. Reserved route for v1; full UI deferred. When implemented: filters for date range, account, kind, shape, commodity, description search, with cursor pagination.

## Split memo

`AccountTransactionSplit.Memo` (≤1024 chars, optional) is a **latent per-leg field** — no v1 editor surfaces it. The per-shape editors expose only the transaction-level memo (`AccountTransaction.Memo`, ≤512), and the split memo is **not** rendered in v1's transaction lists or activity views either. The field exists today so v2 surfaces (filters, exports, list-row annotations, a per-leg note input) can use it without a schema migration.

The server is also free to populate `Memo` for system-generated context — for example, balance-entry reconciliation could write *"Delta vs prior balance entry on 2026-03-31"* on the Adjustments split. Whether to actually write such notes is a per-shape decision; the field accommodates it.

## Audit

Every transaction mutation writes an `auditing.audit_logs` entry via `IAuditLogger`:

- **Create** — one entry per transaction, plus one per linked price row created.
- **Update** — one entry per transaction with field-level diff against `AccountTransactionSnapshot` (which includes splits and their annotations). Linked price row updates emit their own `CommodityPriceSnapshot` diffs.
- **Delete** — one entry per transaction; FK-cascaded splits and linked price rows get explicit deletion entries from the handler.

Background reconciliation passes (re-derived balance-entry splits) audit as transaction updates on the affected balance entries — no separate "reconciliation" event type.

## Future-compatibility

- **Categorised income/expense.** The system `Income` and `Expense` accounts are renamed to v2-appropriate "uncategorised" buckets when user-creatable income/expense accounts arrive. Historical splits referencing them remain valid; existing dividends keep posting to the renamed system account until the user re-categorises.
- **Recurring / scheduled transactions.** A separate `TransactionTemplate` entity holds the shape + cadence; instances materialise as ordinary transactions on schedule. No change to the transaction model.
- **Cost basis & tax lots.** Buy splits already carry quantity and a per-unit price annotation; a `TaxLot` entity references buy splits without modifying them. The annotation is exactly the cost-basis information a tax-lot tracker needs.
- **Tags at split level.** New `SplitTag` join table; no impact on existing data.
- **Soft delete.** Adds `DeletedAt`/`DeletedByUserId` columns; reconciliation passes filter on `DeletedAt IS NULL`.
- **Multi-step buys (external funding in one transaction).** A v2 "Buy from external" sugar shape can produce a 3-split atomic transaction without changing existing two-split buys.
- **Cross-currency dividends with FX.** A USD dividend reinvested via DRIP into a CAD-reporting account can opt into emitting an FX annotation on the cash leg alongside the security annotation on the buy leg — still one annotation per cross-commodity leg.
