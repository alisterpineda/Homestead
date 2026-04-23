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

These are excluded from v1 but the model must not block adding them later. See *Future-compatibility*.

## User-facing concepts

### Accounts

Accounts are the units the user thinks about and manages. Each has a name, a type, and a home in an optional hierarchy (e.g. *Assets → Investments → RRSP*). There are two conceptual shapes, determined by what they hold:

- **Single-position accounts** hold one commodity, typically a currency. Chequing, savings, credit cards, mortgages.
- **Multi-position accounts** hold an open set of commodities simultaneously. Registered and non-registered investment accounts holding cash in multiple currencies plus securities.

This distinction exists for the UI's benefit. In the data model both shapes use the same account representation; a single-position account is a multi-position account that happens to only hold one thing, enforced by convention in the entry UI.

Account types used by v1: `asset`, `liability`, `equity`. The model reserves `income` and `expense` for future use.

Two system-created equity accounts always exist for each household:

- **Equity:Opening Balances** — the counterpart for wealth present before tracking began.
- **Equity:Adjustments** — the counterpart for unexplained monthly reconciliation deltas. See *Balance entries*.

### Commodities

Currencies (CAD, USD, GBP, ...) and securities (VEQT, VFV, AAPL, ...) are represented uniformly. A commodity has a symbol, a display name, a kind (`currency` or `security`), and a display precision. There is no distinction at the model level between a "cash" commodity and a "security" commodity — only behaviour in certain transaction shapes differs.

### Transactions

A transaction is one event at one point in time, balanced in one currency. It has a date, a description, a currency (the currency in which its splits sum to zero), and one or more splits.

Users do not create "transactions" directly. They pick a transaction **shape** (see *Transaction shapes*) and fill in its fields. The system assembles the underlying transaction and its splits.

### Splits

A split is the per-account leg of a transaction. It records:

- Which account it affects.
- Which commodity moved.
- The quantity that moved, in the commodity's native units.
- The value of that movement in the transaction's currency.

A transaction balances when the sum of its splits' values equals zero. This invariant is enforced by the data layer, not relied upon as a convention.

Splits are an implementation concept. Users never see the word "split" in v1.

### Balance entries

A balance entry is a user-provided assertion: *"the balance of this account, in this commodity, on this date, was this much."* The system reconciles the assertion by automatically booking the delta between the previous running balance and the asserted value against **Equity:Adjustments** so that the running balance matches the assertion.

Balance entries are the primary way the user records wealth changes in v1. A typical month's activity is a handful of balance entries — one per account from each statement — and a small number of explicit transfer/investment transactions for activity the user wants itemised.

If the user later adds, edits, or removes a transaction dated on or before an existing balance entry, the system must re-reconcile affected balance entries and recompute their reconciling deltas.

### Prices

Price data is required to express balances in the user's display currency. Each price is a tuple of (commodity, quote currency, date, rate). For a USD cash holding viewed in CAD, the rate is the USD→CAD FX rate. For a VEQT holding viewed in CAD, the rate is VEQT's CAD price. The model treats these identically.

v1 requires manual price entry. Missing prices on a given date resolve by carrying forward the most recently known price prior to that date. The UI must indicate staleness where relevant.

Prices are captured forward from when the user first tracks a commodity. New commodities need a price entered on the day they first appear in any transaction.

## Design principles

### Double-entry, quietly

Every change to wealth has a named counterpart; nothing is a one-sided ghost entry. This is the only way to guarantee the data has no holes — every movement is explained by something. That guarantee is essential for a long-horizon tracker where data recorded today will be re-examined years later.

But this implementation detail does not leak into the UI. Users do not see debits, credits, splits, or balancing rules. They see transfers, investments, and balance entries. The model enforces double-entry; the UI enforces the user's mental model.

### No base currency

Transactions balance in whichever currency they naturally balance in. A USD-denominated transaction balances in USD. A CAD-denominated one balances in CAD. Storage records both the native quantity and the value in the transaction's currency.

There is no household-wide "base currency" at the data layer. A **display currency** is stored as a user preference and applied at read time. Changing the display currency re-values everything on read; it does not rewrite any data.

This is load-bearing for the household's relocation scenarios. Moving from Canada to another country must not require re-denominating historical data.

### Single-position vs multi-position is UI-only

The schema supports any account holding any set of commodities. The "single-position" constraint on chequing-like accounts is enforced by the UI's entry forms, not by the data layer. This keeps the model uniform and avoids a second set of tables for investment accounts.

### Categorisation is a v2 concern

The model reserves `income` and `expense` account types and assumes categories will eventually be implemented as accounts of those types. v1 neither creates those accounts nor exposes them to the user. When v2 arrives, its addition is purely additive — no data migration is required.

### Accumulate prices and history from day one

Net-worth charts begin from the user's earliest recorded event. New commodities' prices are captured on first appearance. There is no concept of "backfill history to give me a nicer chart" — the tracker reflects only what the user actually captured.

### Data integrity and audit trail

Edits and deletes are destructive from the user's perspective — there is no soft delete, no undo, and no user-visible change-history UI in v1. But every mutation *is* recorded to the shared `auditing.audit_logs` table (field-level diffs via `IAuditLogger`, matching the Documents module). Auditing is not exposed to the user; it is a back-end integrity net, cheap to keep and impossible to backfill if we ever want it later. A user-facing change-history view remains a future concern.

This suits a single-household tool where the user and the auditor are the same person. Multi-tenant or shared scenarios will still need an access strategy layered on top before shipping.

## Transaction shapes (user-facing)

The UI exposes a small, closed set of shapes. Each shape is a template that produces a specific transaction structure behind the scenes. The full set for v1:

### Opening balance

Captured at onboarding per account. Creates a transaction with one split on the account and one split on Equity:Opening Balances. User sees this only during setup.

### Transfer

Movement between two of the household's own accounts. The UI asks for source account, destination account, amount, currency (inferred from source), date, and optional description. Cross-currency transfers also ask for the destination amount; the resulting two splits carry different commodities but balance in one of the two currencies.

### Investment buy

Purchase of a security. The UI asks for the investment account, the security, quantity, per-unit price, total (derived), the funding account (typically cash within the same investment account), and date. Produces a two-split transaction: cash out, security in.

### Investment sell

Reverse of buy.

### Dividend

Cash dividend received into an investment account. UI asks for the investment account, commodity the dividend was paid in, amount, and date. Produces a transaction crediting Income (reserved for v2; for v1, credits Equity:Adjustments so as not to surface the income account concept).

### DRIP / reinvestment

Dividend-reinvestment variant that results in additional security units rather than cash. Modelled as either one combined transaction or two sequential transactions (dividend in cash, then cash used to buy more units). Product preference for v1: two sequential transactions, matching what brokerage statements show.

### Stock split

Corporate action that changes commodity quantities without changing value. Modelled as a transaction with relaxed balance enforcement (a single split, value zero, quantity non-zero). Only `stock_split` transactions may skip the balance invariant.

### Balance entry

User asserts the balance of one account and commodity on a given date. System computes the delta and books it against Equity:Adjustments. See *Balance entries*.

## Core computed values

All derived from splits and prices at read time. None are cached at the data layer in v1.

- **Position** (per account, per commodity, as of a date): sum of split quantities.
- **Account value** (per account, as of a date, in display currency): sum over positions of (quantity × carry-forward price in display currency).
- **Net worth** (as of a date, in display currency): sum of asset-account values, less sum of liability-account values.
- **Equity:Adjustments balance** (in display currency): surfaced in the UI as *"Unrecorded wealth changes."* Useful both as a running total and as a tracking-quality signal — a growing balance relative to explicit transactions means the user is relying more on reconciliation and less on itemisation.

## Distinctive UX decisions worth preserving

1. **Balance entries are first-class.** They are not a utility hidden behind a reconciliation menu. They are a primary way to record monthly activity and appear prominently in account detail views.
2. **Sign conventions are hidden.** Liability balances show as positive amounts owed, not negative assets. Dividends show as positive inflows, not negative income-account credits.
3. **No debit/credit terminology in the UI, anywhere, ever.**
4. **Users pick a transaction shape, not a transaction.** The underlying balanced transaction is constructed from the shape.
5. **The display currency is the user's reality.** Every balance, every total, every chart axis is in display currency by default. Native-currency amounts appear where faithful (positions held in a multi-commodity account show native quantity and display-currency value side by side).
6. **Equity:Adjustments is visible.** The UI surfaces its running balance as a metric. A large balance is not presented as an error — it is presented as context about tracking style.

## Future-compatibility

The following must be addable later without rewriting history:

- Income and expense accounts as categories; category reporting and drill-down.
- Tags at the split level for cross-cutting labels (e.g. tax-relevant, trip-specific).
- Adjusted cost basis and tax-lot tracking for non-registered accounts.
- Automated price ingestion from external providers.
- Multi-commodity income/expense accounts (relevant post-relocation when expenses might naturally denominate in multiple currencies).
- Scheduled and recurring transactions.
- Soft delete and user-facing change-history views (the back-end audit log already exists).
- Shared/multi-user households with permissions.

None of these should require re-migrating historical transaction data.
