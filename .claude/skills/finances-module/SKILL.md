---
name: finances-module
description: Product-level requirements and design decisions for the Homestead Finances module — the household net-worth tracking subsystem covering accounts, commodities, transactions, balance assertions, investment positions, and net-worth computation. Consult this whenever designing, implementing, modifying, or reviewing any functionality related to personal finance tracking, account modelling, investment positions, balance reconciliation, multi-currency handling, or net-worth reporting in Homestead, including API design, database schema, UI flows, and naming conventions. Use this even when the work seems adjacent — changes to account structure, currency display, transaction shapes, or equity plug handling should all be reconciled against this document first.
---

# Finances Module — Requirements

This document describes the Finances module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level. Technology choices (language, framework, database) are elsewhere.

Homestead started as a document-management tool and is growing into a broader household suite; Finances sits alongside the existing Documents, Identity, Admin, Processing, and Status modules. Backend code belongs under `Homestead.WebApi/Modules/Finances/` and `Homestead.WebApi/Domain/Finances/`, following the conventions already in use for the other modules; the frontend lives under `Homestead.WebApp/src/app/modules/finances/`. This file governs *what* that module should do, not *how* it is wired up — defer to existing module patterns in the codebase for structure.

## Purpose

The Finances module is the household's net-worth tracking system. It models financial accounts — chequing, savings, credit cards, mortgages, registered and non-registered investment accounts — and the movements between them. The primary outcome is a reliable, long-horizon view of household net worth in a chosen display currency, plus the ability to reconcile against institutional statements each month.

The module is not a full personal-finance tool. It is not an expense tracker, a budget, a tax tool, or a brokerage. It models wealth as state, reconciled periodically against external reality.

## In scope for v1

- Household accounts covering cash, credit, debt, and investments.
- Balances denominated in any currency.
- Investment accounts that hold multiple commodities (currencies and securities) simultaneously.
- A small, fixed set of transaction shapes: transfers between owned accounts, investment purchases and sales, dividends and DRIPs, stock splits, and balance entries.
- Monthly reconciliation against institutional statements as the primary recurring user action.
- Historical net-worth computation and display in a user-chosen currency.
- Manual commodity price entry.

## Out of scope for v1

- Categories, category reporting, expense tracking in the conventional sense.
- Tags on transactions or splits.
- Recurring or scheduled transactions.
- Budgeting.
- Automated price feeds.
- Soft delete, user-visible change history, undo. (Back-end audit logs *are* written — see *Data integrity and audit trail*.)
- Multi-user permissions.
- Adjusted cost basis, tax-lot tracking, realized and unrealized gains for tax reporting.
- Backfill of historical prices for periods before the user actually held the commodity.
- Inline commodity creation from transaction flows — securities are created via `/finances/settings/commodities` first.
- Configurable price-staleness thresholds — v1 renders staleness on a fixed heuristic.

These are excluded from v1 but the model must not block adding them later. See *Future-compatibility*.

## User-facing concepts

### Accounts

Accounts are the units the user thinks about and manages. Users pick from a closed set of user-facing **kinds** — Cash, Credit Card, Loan, Other Liability, Investment, Other Asset — which determine the underlying Asset/Liability classification and whether the account holds one commodity or many. Kind and reporting currency are immutable after creation; everything else (name, institution, archived) is editable. Accounts are a flat list in v1 — the list groups by Kind, and Institution provides secondary grouping where users need it.

Account types used at the model layer in v1: `asset`, `liability`, `equity`, `income`, `expense`. v1 uses `income` and `expense` only for two hidden system accounts — user-creatable income/expense accounts (and the categorisation features they enable) are deferred to v2.

Four system accounts always exist per household, all hidden from user-facing lists:

- **Equity:Opening Balances** — counterpart for wealth present before tracking began.
- **Equity:Adjustments** — counterpart for unexplained balance-entry deltas. See *Balance entries*.
- **Income** — counterpart for cash dividends in v1. Subsumed by user-created income accounts in v2.
- **Expense** — reserved; no v1 shape posts to it. Seeded so v2 doesn't have to backfill.

See [references/accounts.md](references/accounts.md) for the full account-management design: kind taxonomy, creation flow, initial-balance mechanics, sign conventions, opening-balance editing, add-position flow for Investment accounts, archive/delete rules, and the list/detail UI.

### Commodities

Currencies (CAD, USD, GBP, ...) and securities (VEQT, VFV, AAPL, ...) are represented uniformly as **commodities**. Each has a symbol, a display name, a kind (`currency` or `security`), a display precision, and — for securities — an exchange qualifier (`NASDAQ`, `TSX`, `OTC`, ...). All ISO 4217 currencies are seeded on initialisation and fully read-only; users only create securities. Symbol, exchange, and kind are immutable after creation; name and display precision are editable.

See [references/commodities.md](references/commodities.md) for the full design: seeding, creation flow, the exchange qualifier, edit/delete rules, and the list/detail UI.

### Transactions

A transaction is one event at one point in time. It has an **occurred-at** timestamp (`DateTimeOffset`, stored UTC), a description, and one or more splits. There is no transaction-level currency — a transaction balances per-commodity, with price annotations on individual splits bridging cross-commodity movements.

Non-investment transactions collect a date only and store time as `00:00:00 UTC`. Investment transactions default to `12:00:00 UTC` on the chosen date and expose an optional time input for users who want intra-day precision. Balance entries store time as `23:59:59 UTC` so a balance-on-date trumps same-date activity during carry-forward.

Users do not create "transactions" directly. They pick a transaction **shape** (see *Transaction shapes*) and fill in its fields. The system assembles the underlying transaction and its splits.

### Splits

A split is the per-account leg of a transaction. It records:

- Which account it affects.
- Which commodity moved.
- The quantity that moved, in the commodity's native units.
- Optionally, a **unit-price annotation** — a per-unit rate in a quote commodity — that bridges this leg into another commodity for balance purposes. Carried by legs that cross commodity boundaries (a Buy's security leg, a cross-currency Transfer's source leg, etc.); omitted for legs that balance per-commodity against another split.

A transaction balances when, for each commodity, the sum of split **weights** in that commodity is zero. A split's weight is `Quantity` in `Split.Commodity` if it has no price annotation, or `Quantity × Rate` in the annotation's quote commodity if it does. Same-commodity transactions balance trivially per-commodity; cross-commodity transactions rely on at least one annotation to bridge. This invariant is enforced by the data layer; stock splits are the sole declared exception.

Splits are an implementation concept. Users never see the words "split" or "annotation" in v1.

See [references/transactions.md](references/transactions.md) for the full design: shape↔storage-type mapping, worked examples for every shape, balance-entry reconciliation, edit/delete semantics, validation rules, and time/ordering semantics.

### Balance entries

A balance entry is a user-provided assertion: *"the balance of this account, in this commodity, on this date, was this much."* The system reconciles the assertion by automatically booking the delta between the previous running balance and the asserted value against **Equity:Adjustments** so that the running balance matches the assertion.

Balance entries are the primary way the user records wealth changes in v1. A typical month's activity is a handful of balance entries — one per account from each statement — and a small number of explicit transfer/investment transactions for activity the user wants itemised.

If the user later adds, edits, or removes a transaction dated on or before an existing balance entry, the system must re-reconcile affected balance entries and recompute their reconciling deltas.

### Prices

Price data is required to express balances in the user's display currency. Each price is a tuple of `(commodity, quote currency, priceAt, rate)` — *"1 unit of commodity was worth rate units of quote currency at priceAt."* USD→CAD FX rates and VEQT-in-CAD security prices are represented identically. `priceAt` is a `DateTimeOffset` (stored UTC), giving stable same-day ordering and alignment with transaction timestamps.

Prices come from two sources — **manual** entry and **transaction-linked** side-effects derived from split price annotations. Every cross-commodity split annotation (on a Buy's security leg, a cross-currency Transfer's source leg, a DRIP's buy half, an optionally-priced security opening balance) materialises one `CommodityPrice` row. Transaction-linked prices are edited through the source transaction and are read-only in the prices UI; manual prices are editable directly (rate only). Missing prices on a given date resolve by carrying forward the most recently known price, with up to two-hop chaining through intermediary currencies. Holdings with no resolvable price render as *unpriced* — never silently zero.

Prices are captured forward from when the user first tracks a commodity. New commodities need a price entered on the day they first appear in any transaction.

See [references/commodities.md](references/commodities.md) for the full design: the source model, which transaction shapes create prices, carry-forward and chaining rules, collision handling, and the missing-price UX.

## Design principles

### Double-entry, quietly

Every change to wealth has a named counterpart; nothing is a one-sided ghost entry. This is the only way to guarantee the data has no holes — every movement is explained by something. That guarantee is essential for a long-horizon tracker where data recorded today will be re-examined years later.

But this implementation detail does not leak into the UI. Users do not see debits, credits, splits, or balancing rules. They see transfers, investments, and balance entries. The model enforces double-entry; the UI enforces the user's mental model.

### No base currency

Transactions balance per-commodity, with price annotations on individual splits bridging cross-commodity legs. A same-currency transfer balances in that currency; a cross-currency transfer carries a price annotation on one leg that resolves the bridge. No split carries a "value in a transaction currency" field — the transaction has no privileged currency.

There is no household-wide "base currency" at the data layer. A **display currency** is stored as a user preference and applied at read time. Changing the display currency re-values everything on read; it does not rewrite any data.

This is load-bearing for the household's relocation scenarios. Moving from Canada to another country must not require re-denominating historical data.

### Beancount-style sign convention

Split quantities follow the Beancount convention: asset and expense splits are positive when the balance increases; liability, equity, and income splits are negative when the balance increases. For each commodity, the sum of split weights is zero (stock splits exempted). This makes net worth a straight sum of asset and liability balances with no type-conditional arithmetic. The UI flips signs on liabilities so users always see positive "amount owed" and only ever enters positive numbers. See [references/accounts.md](references/accounts.md) and [references/transactions.md](references/transactions.md#sign-convention) for worked examples.

### Single-position vs multi-position is UI-only

The schema supports any account holding any set of commodities. The "single-position" constraint on Cash / Credit Card / Loan / Other kinds is enforced by the UI — through the set of transaction shapes offered for each kind — not by the data layer. Only Investment accounts surface multi-commodity shapes. This keeps the model uniform and avoids a second set of tables for investment accounts.

### Categorisation is a v2 concern

The model reserves `income` and `expense` account types and assumes categories will eventually be implemented as user-creatable accounts of those types. v1 seeds two hidden system accounts (`Income`, `Expense`) for shapes that need a categorised counterpart (currently just Dividend → Income), but does not let users create their own and does not surface category reporting. When v2 arrives, the system accounts are renamed to "uncategorised" buckets and user-creatable categories slot in alongside — no historical data is rewritten.

### Accumulate prices and history from day one

Net-worth charts begin from the user's earliest recorded event. New commodities' prices are captured on first appearance. There is no concept of "backfill history to give me a nicer chart" — the tracker reflects only what the user actually captured.

### Data integrity and audit trail

Edits and deletes are destructive from the user's perspective — there is no soft delete, no undo, and no user-visible change-history UI in v1. But every mutation *is* recorded to the shared `auditing.audit_logs` table (field-level diffs via `IAuditLogger`, matching the Documents module). Auditing is not exposed to the user; it is a back-end integrity net, cheap to keep and impossible to backfill if we ever want it later. A user-facing change-history view remains a future concern.

This suits a single-household tool where the user and the auditor are the same person. Multi-tenant or shared scenarios will still need an access strategy layered on top before shipping.

## Transaction shapes (user-facing)

The UI exposes a small, closed set of shapes. Each shape is a template that produces a specific transaction structure behind the scenes. The full set for v1 is summarised below; see [references/transactions.md](references/transactions.md#transaction-shapes) for worked examples, validation rules, and the shape-availability matrix per account kind.

### Opening balance

Captures wealth that existed before tracking began. For single-position kinds (Cash, Credit Card, Loan, Other Liability, Other Asset) a single opening balance is captured at account creation. For Investment accounts an opening balance is captured per commodity via the *Add position* action on the account detail page — each position's opening balance is independent, one transaction per `(account, commodity)` pair. All opening balances produce a two-split transaction against `Equity:Opening Balances`. Opening-balance transactions are edited through dedicated per-account / per-position affordances, not through the generic transaction editor. See [references/accounts.md](references/accounts.md).

### Transfer

Movement between two of the household's own accounts. The UI asks for source account, destination account, amount, currency (inferred from source), date, and optional description. Cross-currency transfers also ask for the destination amount; the resulting two splits carry different commodities, and the source leg carries a price annotation giving the implied rate. That annotation also materialises as a `CommodityPrice` row.

### Investment buy

Purchase of a security. The UI asks for the investment account, the security, quantity, per-unit price, total (derived), the funding account (typically cash within the same investment account), and date. Produces a two-split transaction: cash out, security in.

### Investment sell

Reverse of buy.

### Dividend

Cash dividend received into an investment account. UI asks for the investment account, commodity the dividend was paid in, amount, and date. Produces a two-split transaction crediting the hidden system `Income` account. v2 categorised income accounts subsume this without rewriting history.

### DRIP / reinvestment

Dividend-reinvestment variant that results in additional security units rather than cash. Modelled as two linked `NormalTransaction`s at the same `OccurredAt` — a Dividend half and a Buy half, paired via `RelatedTransactionId`. This matches brokerage statements and keeps each half an ordinary shape. The generic editor surfaces the pair as one logical event.

### Stock split

Corporate action that changes commodity quantities without changing value. Scoped **per commodity** — one transaction covers every account holding the security, with one split per holding account. Stock splits are the sole declared exception to the per-commodity balance invariant: quantities deliberately do not sum to zero (that is the point of the operation), and no price annotation is present.

### Balance entry

User asserts the balance of one account and commodity on a given date. System computes the delta and books it against Equity:Adjustments. See *Balance entries*.

## Core computed values

All derived from splits and prices at read time. None are cached at the data layer in v1.

- **Position** (per account, per commodity, as of a date): sum of split quantities.
- **Account value** (per account, as of a date, in display currency): sum over positions of (quantity × carry-forward price in display currency).
- **Net worth** (as of a date, in display currency): sum of asset-account values, less sum of liability-account values.
- **Equity:Adjustments balance** (in display currency): surfaced in the UI as *"Unrecorded wealth changes."* Useful both as a running total and as a tracking-quality signal — a growing balance relative to explicit transactions means the user is relying more on reconciliation and less on itemisation.

See [references/valuation.md](references/valuation.md) for the API contract: which valuations are inline on the account resource, which live on sub-resources, the `null` vs `0` rule for unpriced holdings, and what is deliberately excluded from v1.

## Distinctive UX decisions worth preserving

1. **Balance entries are first-class.** They are not a utility hidden behind a reconciliation menu. They are a primary way to record monthly activity and appear prominently in account detail views.
2. **Sign conventions are hidden.** Liability balances show as positive amounts owed, not negative assets. Dividends show as positive inflows, not negative income-account credits.
3. **No debit/credit terminology in the UI, anywhere, ever.**
4. **Users pick a transaction shape, not a transaction.** The underlying balanced transaction is constructed from the shape.
5. **The display currency is the user's reality.** Every balance, every total, every chart axis is in display currency by default. Native-currency amounts appear where faithful (positions held in a multi-commodity account show native quantity and display-currency value side by side).
6. **Equity:Adjustments is visible.** The UI surfaces its running balance as a metric. A large balance is not presented as an error — it is presented as context about tracking style.

## Future-compatibility

The following must be addable later without rewriting history:

- Income and expense accounts as categories; category reporting and drill-down. This is also when a parent-child account hierarchy would be introduced (categories are naturally hierarchical, e.g. Food → Groceries → Produce).
- Tags at the split level for cross-cutting labels (e.g. tax-relevant, trip-specific).
- Adjusted cost basis and tax-lot tracking for non-registered accounts.
- Automated price ingestion from external providers.
- Multi-commodity income/expense accounts (relevant post-relocation when expenses might naturally denominate in multiple currencies).
- Scheduled and recurring transactions.
- Soft delete and user-facing change-history views (the back-end audit log already exists).
- Shared/multi-user households with permissions.

None of these should require re-migrating historical transaction data.
