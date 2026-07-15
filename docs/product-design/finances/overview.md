# Finances — Requirements

This document describes the Finances module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level. Technology choices (language, framework, database) are elsewhere — see the repo's CLAUDE.md files for those.

Finances sits alongside Documents and the cross-cutting infrastructure modules listed in the [product-design index](../README.md). This file governs *what* the module should do; defer to existing module patterns in the codebase for *how* it is wired up.

## Purpose

The Finances module is the household's net-worth tracking system. It models financial accounts — chequing, savings, credit cards, mortgages, registered and non-registered investment accounts — and the movements between them. The primary outcome is a reliable, long-horizon view of household net worth in a chosen display currency, plus the ability to reconcile against institutional statements each month.

The module is not a full personal-finance tool. It is not an expense tracker, a budget, a tax tool, or a brokerage. It models wealth as state, reconciled periodically against external reality.

## In scope for v1

- Household accounts covering cash, credit, debt, and investments.
- Balances denominated in any currency.
- Investment accounts that hold multiple commodities (currencies and securities) simultaneously.
- A small, fixed set of transaction shapes: everyday expenses and income (spend/refunds), transfers between owned accounts, investment purchases and sales, dividends and DRIPs, and balance entries.
- Monthly reconciliation against institutional statements as the primary recurring user action.
- Historical net-worth computation and display in a user-chosen currency.
- Commodity prices: manual entry **and** automated import from external sources (e.g. Bank of Canada Valet for FX rates, Alpha Vantage for equity quotes), with per-source scheduling and an admin management UI — see [commodities.md](commodities.md#price-sources--model).

## Out of scope for v1

- Category **reporting / drill-down** (a by-category spend report) — the *categorisation model and assignment* shipped in cut C-1 (see *Categorisation* below), but the report is a fast-follow (C-2) and is not built yet. Categorisation is **optional**: flows default to Uncategorised and income/expense balances stay out of net worth, so anyone who ignores categories sees the v1 tracker unchanged.
- Tags on transactions or splits.
- Recurring or scheduled transactions.
- Budgeting.
- Corporate actions on securities (stock splits, spin-offs, mergers, ticker changes). Households that experience one mid-v1 work around it by recording a balance entry on the effective date asserting the new unit count (the delta lands in `Equity:Adjustments`) **and** a new price observation at the post-action rate — both steps are required, otherwise carry-forward of the pre-action price against the new unit count distorts net worth. The workaround leaves residue in `Equity:Adjustments`; v2's commodity-scoped corporate-action model should include a one-time pass to retire those phantom splits in favour of a real action row. See *Future-compatibility*.
- Soft delete, user-visible change history, undo. (Back-end audit logs *are* written — see *Data integrity and audit trail*.)
- Multi-user permissions.
- Adjusted cost basis, tax-lot tracking, realized and unrealized gains for tax reporting.
- Backfill of historical prices for periods before the user actually held the commodity.
- Inline commodity creation from transaction flows — securities are created via `/finances/commodities` first.
- Configurable price-staleness thresholds — v1 renders staleness on a fixed heuristic.

These are excluded from v1 but the model must not block adding them later. See *Future-compatibility*.

## User-facing concepts

### Accounts

Accounts are the units the user thinks about and manages. Users pick from a closed set of user-facing **kinds** — Cash, Credit Card, Loan, Other Liability, Investment, Other Asset — which determine the underlying Asset/Liability classification and whether the account holds one commodity or many. Kind and reporting currency are immutable after creation; everything else (name, institution, archived) is editable. Accounts are a flat list in v1 — the list groups by Kind, and Institution provides secondary grouping where users need it.

Account types used at the model layer: `asset`, `liability`, `equity`, `income`, `expense`. `income`/`expense` accounts are **categories** — as of cut C-1 users can create their own (nested), and a flow may post to a category instead of the hidden system bucket. Only `asset`/`liability` accounts are "balance-sheet accounts" that appear in the account list and sum into net worth; `income`/`expense` categories are managed separately (see *Categorisation*) and never enter net worth. The seeded `Income`/`Expense` system accounts remain as the read-only "Uncategorised" buckets.

Four system accounts always exist per household, all hidden from user-facing lists:

- **Equity:Opening Balances** — counterpart for wealth present before tracking began.
- **Equity:Adjustments** — counterpart for unexplained balance-entry deltas. See *Balance entries*.
- **Income** — the read-only **Uncategorised Income** bucket: counterpart for cash dividends and any income/refund flow the user hasn't categorised.
- **Expense** — the read-only **Uncategorised Expense** bucket: counterpart for any spend flow the user hasn't categorised.

See [accounts.md](accounts.md) for the full account-management design: kind taxonomy, creation flow, initial-balance mechanics, sign conventions, opening-balance editing, add-position flow for Investment accounts, archive/delete rules, and the list/detail UI.

### Commodities

Currencies (CAD, USD, GBP, ...) and securities (VEQT, VFV, AAPL, ...) are represented uniformly as **commodities**. Each has a symbol, a display name, a kind (`currency` or `security`), a display precision, and — for securities — an exchange qualifier (`NASDAQ`, `TSX`, `OTC`, ...). All ISO 4217 currencies are seeded on initialisation and fully read-only; users only create securities. Symbol, exchange, and kind are immutable after creation; name and display precision are editable.

See [commodities.md](commodities.md) for the full design: seeding, creation flow, the exchange qualifier, edit/delete rules, and the list/detail UI.

### Transactions

A transaction is one event at one point in time. It has an **occurred-at** timestamp (`DateTimeOffset`, stored UTC), a description, and one or more splits. There is no transaction-level currency — a transaction balances per-commodity, with price annotations on individual splits bridging cross-commodity movements.

Non-investment transactions collect a date only and store time as `00:00:00 UTC`. Investment transactions default to `12:00:00 UTC` on the chosen date; the Investment Buy/Sell and Dividend editors now expose an optional time input for intra-day precision (a collapsible "Add time" control — a date-only entry composes to the `12:00 UTC` anchor, while an explicit time is interpreted as the browser's local wall clock and stored UTC). The *Add position* (opening-balance) flow still collects a date only and uses the fixed `12:00 UTC` default. Balance entries store time as `23:59:59 UTC` so a balance-on-date trumps same-date activity during carry-forward.

Users do not create "transactions" directly. They pick a transaction **shape** (see *Transaction shapes*) and fill in its fields. The system assembles the underlying transaction and its splits.

### Splits

A split is the per-account leg of a transaction. It records:

- Which account it affects.
- Which commodity moved.
- The quantity that moved, in the commodity's native units.
- Optionally, a **unit-price annotation** — a per-unit rate in a quote commodity — that bridges this leg into another commodity for balance purposes. Carried by legs that cross commodity boundaries (a Buy's security leg, a cross-currency Transfer's source leg, etc.); omitted for legs that balance per-commodity against another split.

A transaction balances when, for each commodity, the sum of split **weights** in that commodity is zero. A split's weight is `Quantity` in `Split.Commodity` if it has no price annotation, or `Quantity × Rate` in the annotation's quote commodity if it does. Same-commodity transactions balance trivially per-commodity; cross-commodity transactions rely on at least one annotation to bridge. This invariant is enforced by the data layer with no exceptions in v1.

Splits are an implementation concept. Users never see the words "split" or "annotation" in v1.

See [transactions.md](transactions.md) for the full design: shape↔storage-type mapping, worked examples for every shape, balance-entry reconciliation, edit/delete semantics, validation rules, and time/ordering semantics.

### Balance entries

A balance entry is a user-provided assertion: *"the balance of this account, in this commodity, on this date, was this much."* The system reconciles the assertion by automatically booking the delta between the previous running balance and the asserted value against **Equity:Adjustments** so that the running balance matches the assertion.

Balance entries are the primary way the user records wealth changes in v1. A typical month's activity is a handful of balance entries — one per account from each statement — and a small number of explicit transfer/investment transactions for activity the user wants itemised.

If the user later adds, edits, or removes a transaction dated on or before an existing balance entry, the system must re-reconcile affected balance entries and recompute their reconciling deltas.

### Prices

Price data is required to express balances in the user's display currency. Each price is a tuple of `(commodity, quote currency, priceAt, rate)` — *"1 unit of commodity was worth rate units of quote currency at priceAt."* USD→CAD FX rates and VEQT-in-CAD security prices are represented identically. `priceAt` is a `DateTimeOffset` (stored UTC), giving stable same-day ordering and alignment with transaction timestamps.

Prices come from two sources — **manual** entry and **transaction-linked** side-effects derived from split price annotations. Every cross-commodity split annotation (on a Buy's security leg, a cross-currency Transfer's source leg, a DRIP's buy half, an optionally-priced security opening balance) materialises one `CommodityPrice` row. Transaction-linked prices are edited through the source transaction and are read-only in the prices UI; manual prices are editable directly (rate and `priceAt`). Missing prices on a given date resolve by carrying forward the most recently known price, with up to two-hop chaining through intermediary currencies. Holdings with no resolvable price render as *unpriced* — never silently zero.

Prices are captured forward from when the user first tracks a commodity. New commodities need a price entered on the day they first appear in any transaction.

See [commodities.md](commodities.md) for the full design: the source model, which transaction shapes create prices, carry-forward and chaining rules, collision handling, and the missing-price UX.

## Design principles

### Double-entry, quietly

Every change to wealth has a named counterpart; nothing is a one-sided ghost entry. This is the only way to guarantee the data has no holes — every movement is explained by something. That guarantee is essential for a long-horizon tracker where data recorded today will be re-examined years later.

But this implementation detail does not leak into the UI. Users do not see debits, credits, splits, or balancing rules. They see transfers, investments, and balance entries. The model enforces double-entry; the UI enforces the user's mental model.

### No base currency

Transactions balance per-commodity, with price annotations on individual splits bridging cross-commodity legs. A same-currency transfer balances in that currency; a cross-currency transfer carries a price annotation on one leg that resolves the bridge. No split carries a "value in a transaction currency" field — the transaction has no privileged currency.

There is no household-wide "base currency" at the data layer. A **display currency** is stored as a single household-wide setting (`FinanceSettings`, one row per household — not per user) and applied at read time; see [valuation.md](valuation.md#accountresponse-additions). Changing the display currency re-values everything on read; it does not rewrite any data. (Per-user display-currency selection would be a deliberate v2 multi-user feature; it is not built in v1, where multi-user is out of scope.)

This is load-bearing for the household's relocation scenarios. Moving from Canada to another country must not require re-denominating historical data.

### Beancount-style sign convention

Split quantities follow the Beancount convention: asset and expense splits are positive when the balance increases; liability, equity, and income splits are negative when the balance increases. For each commodity, the sum of split weights is zero. This makes net worth a straight sum of asset and liability balances with no type-conditional arithmetic. The UI flips signs on liabilities so users always see positive "amount owed" and only ever enters positive numbers. See [accounts.md](accounts.md) and [transactions.md](transactions.md#sign-convention) for worked examples.

### Single-position vs multi-position is UI-only

The schema supports any account holding any set of commodities. The "single-position" constraint on Cash / Credit Card / Loan / Other kinds is enforced by the UI — through the set of transaction shapes offered for each kind — not by the data layer. Only Investment accounts surface multi-commodity shapes. This keeps the model uniform and avoids a second set of tables for investment accounts.

### Categorisation (available, and optional)

Categories are `Account` rows of type `income`/`expense` with `IsSystem=false`, `Kind=null` — the locked "categories-are-accounts" philosophy (a split-level category tag and a separate `Category` entity were both rejected). Cut **C-1** shipped the model + assignment + a nested management page (`/finances/categories`): users create their own categories, nest them (`Food → Groceries → Produce`), and a spend/income flow may post to a category instead of the seeded system bucket. It is **opt-in at every level** — flows default to Uncategorised (the read-only `Income`/`Expense` buckets), income/expense balances stay out of net worth, and the by-category report is a separate C-2 surface — so a household that ignores categories sees the core net-worth tracker unchanged. Deliberately deferred: the by-category **report** (C-2); category **archive**, **merge**, bulk-recategorise, Dividend categorisation, and statement-import assignment (C-3). No historical flow is rewritten — existing flows stay Uncategorised until edited.

The one load-bearing correctness rule this introduced: `!IsSystem` no longer means "balance-sheet account" (a category is `IsSystem=false` too). Every read path that sums or lists "real accounts" filters on the `Asset`/`Liability` predicate, not `!IsSystem`, so categories never leak into the account list or net worth.

### Accumulate prices and history from day one

Net-worth charts begin from the user's earliest recorded event. New commodities' prices are captured on first appearance. There is no concept of "backfill history to give me a nicer chart" — the tracker reflects only what the user actually captured.

### Data integrity and audit trail

Edits and deletes are destructive from the user's perspective — there is no soft delete, no undo, and no user-visible change-history UI in v1. But every mutation *is* recorded to the shared `auditing.audit_logs` table (field-level diffs via `IAuditLogger`, matching the Documents module). Auditing is not exposed to the user; it is a back-end integrity net, cheap to keep and impossible to backfill if we ever want it later. A user-facing change-history view remains a future concern.

This suits a single-household tool where the user and the auditor are the same person. Multi-tenant or shared scenarios will still need an access strategy layered on top before shipping.

## Transaction shapes (user-facing)

The UI exposes a small, closed set of shapes. Each shape is a template that produces a specific transaction structure behind the scenes. The full set for v1 is summarised below; see [transactions.md](transactions.md#transaction-shapes) for worked examples, validation rules, and the shape-availability matrix per account kind.

### Opening balance

Captures wealth that existed before tracking began. For single-position kinds (Cash, Credit Card, Loan, Other Liability, Other Asset) a single opening balance is captured at account creation. For Investment accounts an opening balance is captured per commodity via the *Add position* action on the account detail page — each position's opening balance is independent, one transaction per `(account, commodity)` pair. All opening balances produce a two-split transaction against `Equity:Opening Balances`. Opening-balance transactions are edited through dedicated per-account / per-position affordances, separate from the other shapes' editors. See [accounts.md](accounts.md).

### Transfer

Movement between two of the household's own accounts. The UI asks for source account, destination account, amount, currency (inferred from source), date, and optional description. Cross-currency transfers also ask for the destination amount; the resulting two splits carry different commodities, and the source leg carries a price annotation giving the implied rate. That annotation also materialises as a `CommodityPrice` row.

### Investment buy

Purchase of a security. The UI asks for the investment account, the side (Buy/Sell), the security, quantity, per-unit price, total (derived), the funding currency (defaulting to the account's reporting currency; revealed only when the account holds cash in another currency), and date. Produces a two-split transaction on the same investment account: cash out (in the funding currency), security in. The security leg carries the per-unit price annotation, which also materialises one transaction-linked `CommodityPrice` row.

### Investment sell

Reverse of buy.

### Dividend

Cash dividend received into an investment account. UI asks for the investment account, commodity the dividend was paid in, amount, and date. Produces a two-split transaction crediting the hidden `Income` (Uncategorised) bucket. Dividend categorisation is deferred to C-3, so dividends stay Uncategorised for now.

### Expense / Income

Everyday spending or income on a non-Investment account — the shape for "I spent $50" or "I got a $30 refund". One shared editor with an Expense/Income toggle (mirrors Buy/Sell). UI asks for the account (read-only), direction, amount, date, an optional **category** (C-1; default Uncategorised), and an optional free-text description; the currency is inferred from the account's reporting currency (no picker). Produces a two-split transaction posting the non-account leg to the chosen category or, if none, the hidden `Expense`/`Income` Uncategorised bucket. Available on all non-Investment kinds — Investment income is a Dividend instead. On a liability account an expense reads as a positive charge and income/refund as a negative (red) credit, the statement convention, with no per-account-type sign flip. Net worth is unaffected (the counterpart — bucket or category — never enters net worth). This is also the shape produced by statement line-item import (see *Statement import* in [transactions.md](transactions.md#statement-import)).

### DRIP / reinvestment

Dividend-reinvestment variant that results in additional security units rather than cash. Modelled as two linked `NormalTransaction`s at the same `OccurredAt` — a Dividend half and a Buy half, paired via `RelatedTransactionId`. This matches brokerage statements and keeps each half an ordinary shape. The DRIP editor surfaces the pair as one logical event.

### Balance entry

User asserts the balance of one account and commodity on a given date. System computes the delta and books it against Equity:Adjustments. See *Balance entries*.

## Core computed values

All derived from splits and prices at read time. None are cached at the data layer in v1.

- **Position** (per account, per commodity, as of a date): sum of split quantities.
- **Account value** (per account, as of a date, in display currency): sum over positions of (quantity × carry-forward price in display currency).
- **Net worth** (as of a date, in display currency): sum of asset-account values, less sum of liability-account values.
- **Equity:Adjustments balance** (in display currency): intended to surface in the UI as *"Unrecorded wealth changes"* — useful both as a running total and as a tracking-quality signal (a growing balance relative to explicit transactions means the user is relying more on reconciliation and less on itemisation). _**Planned, not yet built in v1:** the Adjustments account is maintained internally by the reconciler, but no read endpoint returns its balance and no UI surfaces it yet._

See [valuation.md](valuation.md) for the API contract: which valuations are inline on the account resource, which live on sub-resources, the `null` vs `0` rule for unpriced holdings, and what is deliberately excluded from v1.

**Itemized statement import** is now available for bank/credit-card accounts: an uploaded statement is parsed and, on one atomic review-and-commit, becomes a closing-balance assertion **and** an itemized set of uncategorised Expense/Income flows (with an advisory reconciliation checksum). See *Statement import* in [transactions.md](transactions.md#statement-import). Brokerage **holdings → balance-entries** import stays deferred; investment statements are gated off at initiate.

## Distinctive UX decisions worth preserving

1. **Balance entries are first-class.** They are a primary transaction shape — selectable from the New-transaction action alongside Transfer, not relegated to a separate "reconcile account" tool — and a primary way to record monthly activity. They appear inline in the account's Activity list, differentiated by label rather than separated out.
2. **Sign conventions are hidden.** Liability balances show as positive amounts owed, not negative assets. Dividends show as positive inflows, not negative income-account credits.
3. **No debit/credit terminology in the UI, anywhere, ever.**
4. **Users pick a transaction shape, not a transaction.** The underlying balanced transaction is constructed from the shape.
5. **The display currency is the user's reality.** Every balance, every total, every chart axis is in display currency by default. Native-currency amounts appear where faithful (positions held in a multi-commodity account show native quantity and display-currency value side by side).
6. **Equity:Adjustments is visible.** _(Planned, not yet built in v1.)_ The UI is intended to surface its running balance as a metric. A large balance is not presented as an error — it is presented as context about tracking style.

## Future-compatibility

The following must be addable later without rewriting history:

- ~~Income and expense accounts as categories~~ — **shipped (C-1)**: nested user categories with a management page and per-flow assignment. Still ahead on this axis: category **reporting / drill-down** (C-2) and category archive/merge/bulk-recategorise (C-3). The parent-child hierarchy introduced here is scoped to categories only, not balance-sheet accounts.
- Tags at the split level for cross-cutting labels (e.g. tax-relevant, trip-specific).
- Adjusted cost basis and tax-lot tracking for non-registered accounts.
- Additional automated price providers beyond the shipped Bank of Canada Valet and Alpha Vantage integrations (see in-scope list above).
- Multi-commodity income/expense accounts (relevant post-relocation when expenses might naturally denominate in multiple currencies).
- Scheduled and recurring transactions.
- Soft delete and user-facing change-history views (the back-end audit log already exists).
- Shared/multi-user households with permissions.
- **Corporate actions on securities** (stock splits, spin-offs, mergers, ticker changes). When introduced, modelled as **per-commodity events**, not transactions: a `CommodityCorporateAction` row carries `(commodityId, type, effectiveAt, ratio, …)` and applies at read time — every position in the affected commodity is scaled by the cumulative ratio of qualifying actions, and historical price observations on the commodity are scaled identically so quantity × price stays coherent. No per-account splits materialise, so a back-dated trade entered after the action picks up the scaling automatically. Per-account activity views derive a virtual display row when the account held the commodity at `effectiveAt`. This shape avoids the need to special-case the per-commodity weight-balance invariant, which is why corporate actions are deferred rather than retrofitted into the transaction model.

None of these should require re-migrating historical transaction data.
