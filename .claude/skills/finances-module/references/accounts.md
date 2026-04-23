# Accounts — Reference

Detailed design for account management in the Finances module. Expands on the summary in [SKILL.md](../SKILL.md#accounts). Read this before designing, implementing, or reviewing any account-related functionality — account kinds, creation, initial balances, opening-balance edits, add-position flow for investments, archive/delete rules, or the list/detail UI.

## Conceptual model

Accounts are the units the user thinks about and manages. Each has a name, a user-facing **kind**, a reporting currency, and an optional institution. Accounts form a flat list in v1; a parent-child hierarchy is deferred to v2 when income/expense categories are added (categories are the feature that actually needs hierarchy).

There are two internal shapes, determined by kind:

- **Single-position accounts** hold one commodity. Cash, Credit Cards, Loans, Other Liabilities, Other Assets.
- **Multi-position accounts** hold an open set of commodities. Investments only.

The single-position constraint is a UI rule, not a data-layer rule: the schema supports any account holding any set of commodities. The UI enforces the constraint by offering only single-commodity transaction shapes for single-position kinds.

## Account kinds

Users never pick `Asset`/`Liability` or "multi-position" directly. They pick a **Kind**, which determines the internal classification and the shapes available on the account.

| Kind            | Type      | Multi-position | Typical uses                                            |
|-----------------|-----------|----------------|---------------------------------------------------------|
| Cash            | Asset     | No             | Chequing, savings, physical cash, prepaid / gift cards  |
| Credit Card     | Liability | No             | Credit cards — displayed as positive "amount owed"      |
| Loan            | Liability | No             | Mortgage, student loan, auto loan, personal loan        |
| Other Liability | Liability | No             | Money owed to family, taxes due, lease obligations      |
| Investment      | Asset     | Yes            | RRSP, TFSA, non-registered brokerage, employer RSU plan |
| Other Asset     | Asset     | No             | House, vehicle, collectibles — value-asserted           |

**Immutable after creation:** Kind, reporting currency. Both change the meaning of historical data; changing them is not supported in v1. A user who picks wrong archives the account and creates a new one.

**Editable:** name, institution, archived flag.

## System accounts

Two system-created equity accounts always exist for each household:

- **Equity:Opening Balances** — counterpart for wealth present before tracking began.
- **Equity:Adjustments** — counterpart for unexplained balance-entry deltas.

Both are multi-position (they must be able to hold opening balances and adjustments in any commodity) and hidden from the account list.

## Sign convention (Beancount-style)

Stored split values follow the Beancount convention:

- Asset splits: positive = increase.
- Liability splits: negative = increase (more owed = more negative).
- Equity splits: negative = increase (initial wealth is booked as a negative equity value).
- `SUM(split.Value) = 0` for every transaction except stock splits.

Consequences:

- Net worth = `SUM(asset balances) + SUM(liability balances)`, with no type-conditional arithmetic.
- The UI flips the sign for liability accounts when displaying balances, so users always see positive "amount owed."
- The UI accepts positive numbers for every kind; the server applies the sign flip when writing splits for liability accounts.

## Creation flow

A single modal, not a wizard. The kind picker plus the form body fits comfortably in one view.

**Always visible:**

1. **Kind picker** — six cards, each with icon, title, and a one-line description.
2. **Name** — required, trimmed, ≤256 characters.
3. **Reporting currency** — required, searchable select. Defaults to the household Display Currency. For Investment accounts, this is the account's statement currency (the currency the brokerage reports it in, and the default for cash balances within it).
4. **Institution** — optional, ≤128 characters.

**Kind-dependent:**

- **Cash, Credit Card, Loan, Other Liability, Other Asset:** require an initial **amount** and **as-of date**. The user enters a positive number; the server applies sign conventions. Default as-of date is today; max is today. No "skip" affordance — users who don't know the current balance can enter 0 and edit later.
- **Investment:** no initial balance field at creation. Positions are added afterward via the *Add position* action on the account detail page.

**Post-submit:** navigate to the account detail page.

## Initial balance mechanics

Creating a non-Investment account with an initial balance emits one opening-balance transaction booked against `Equity:Opening Balances`. Adding a position on an Investment account emits the same shape per commodity.

**Cash account, $1,000 CAD on 2026-04-23:**

```
Transaction: Type=Normal, Description="Opening balance", TxnDate=2026-04-23, Currency=CAD
  Split 1: Account=<cash>,      Commodity=CAD, Quantity=+1000, Value=+1000
  Split 2: Account=<Equity:OB>, Commodity=CAD, Quantity=-1000, Value=-1000
```

**Credit Card, $500 CAD owed on 2026-04-23** — the server flips the sign because Kind is a liability:

```
Transaction: Type=Normal, TxnDate=2026-04-23, Currency=CAD
  Split 1: Account=<credit card>, Commodity=CAD, Quantity=-500, Value=-500
  Split 2: Account=<Equity:OB>,   Commodity=CAD, Quantity=+500, Value=+500
```

**Investment *Add position* — 100 shares of VEQT @ $35.12 CAD on 2026-04-23:**

```
Transaction: Type=Normal, TxnDate=2026-04-23, Currency=CAD
  Split 1: Account=<investment>, Commodity=VEQT, Quantity=+100,  Value=+3512
  Split 2: Account=<Equity:OB>,  Commodity=CAD,  Quantity=-3512, Value=-3512
```

Also emits a `CommodityPrice` row: `(VEQT, CAD, 2026-04-23, 35.12)`.

**Investment *Add position* — $5,000 USD cash on 2026-04-23** (foreign-currency cash in a CAD-reporting account):

```
Transaction: Type=Normal, TxnDate=2026-04-23, Currency=USD
  Split 1: Account=<investment>, Commodity=USD, Quantity=+5000, Value=+5000
  Split 2: Account=<Equity:OB>,  Commodity=USD, Quantity=-5000, Value=-5000
```

Cash opening balances balance in their own currency — no price required. Security opening balances balance in the security's quote currency and require a price-per-unit input (which also creates a `CommodityPrice` row).

**Invariant: one opening-balance transaction per `(account, commodity)` pair.** *Add position* for a commodity that already has an opening balance in that account becomes *Edit opening balance* instead.

## Editing opening balances

Opening-balance transactions are edited only via dedicated per-account (non-Investment) or per-position (Investment) affordances — never through the generic transaction editor.

Rules:

- **Amount / quantity** — always editable.
- **Price per unit** (securities only) — editable; updates the associated `CommodityPrice` row on the same date.
- **As-of date** — editable only if it remains the earliest transaction for its `(account, commodity)` pair. Moving it past later transactions is blocked with a clear error.
- **Delete** — allowed only if no other transactions reference `(account, commodity)`. For a non-Investment account this means "no activity at all." For an Investment position this means "no buys, sells, dividends, or splits touching that commodity."
- **Re-add after delete** — Investment: use *Add position* again. Non-Investment: the detail page exposes an *Add opening balance* action when none exists.

## Add position (Investment only)

Modal: commodity picker · quantity · as-of date. If the commodity is a security, the modal additionally requires a price-per-unit and emits a `CommodityPrice` row. If the selected commodity already has a position in this account, the modal switches to *Edit opening balance* for that position.

## Account list

Route: `/finances/accounts`.

- Header: title + *New Account* action.
- Net-worth summary card: total in Display Currency, with asset / liability subtotals.
- Grouped by Kind, in this order: Cash, Investments, Other Assets, Credit Cards, Loans, Other Liabilities.
- Group header shows the signed group total (liabilities displayed as positive amounts owed).
- Per row: name · institution · balance in display currency · native amount where it differs.
- Archived accounts are hidden by default, collapsed under "Archived (N)" per group.
- Row actions (kebab): Edit, Archive / Unarchive. Delete is offered only when the account has zero transactions.

## Account detail

Route: `/finances/accounts/:id`.

**Header:** breadcrumb · name · kind badge · institution · reporting currency · current balance in display currency, with native amount beside it where different.

**Primary actions:**

- **Record Balance** (balance entry) — the prominent primary action.
- **New Transaction** — dropdown of shapes allowed for this Kind.
- **Add position** — Investment accounts only.

**Tabs:**

- **Activity** — unified chronological list of every transaction affecting this account, including opening balances and balance entries. Differentiated by icon / label, not by separation.
- **Positions** — Investment accounts only. Table: commodity · quantity · native value · display value · share of account. Each row has an *Edit opening balance* action when one exists.
- **Info** — kind, reporting currency, institution, created / updated timestamps.

Archived accounts render read-only. Primary actions disappear; Edit and Unarchive remain.

## Edit / Archive / Delete

**Edit** (modal):
- Editable fields: Name, Institution, Archived flag.
- Immutable fields: Kind, Reporting currency — shown but disabled.

**Archive / Unarchive:** toggles the `IsArchived` flag. Archived accounts are read-only; balance-entry, transaction, and add-position actions are not offered.

**Delete:** allowed only when the account has zero transactions (opening balance included). Otherwise the menu offers Archive only.
