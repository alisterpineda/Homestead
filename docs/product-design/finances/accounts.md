# Accounts — Reference

Detailed design for account management in the Finances module. Expands on the summary in [overview.md](overview.md#accounts). Read this before designing, implementing, or reviewing any account-related functionality — account kinds, creation, initial balances, opening-balance edits, add-position flow for investments, archive/delete rules, or the list/detail UI.

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

Four system-created accounts always exist for each household:

- **Equity:Opening Balances** (`AccountType=Equity`) — counterpart for wealth present before tracking began.
- **Equity:Adjustments** (`AccountType=Equity`) — counterpart for unexplained balance-entry deltas.
- **Income** (`AccountType=Income`) — counterpart for cash dividends in v1. Subsumed by user-created income accounts in v2; see [transactions.md](transactions.md#system-accounts-used-as-counterparts).
- **Expense** (`AccountType=Expense`) — reserved for symmetry; no v1 shape posts to it. Seeded so v2 doesn't have to backfill it.

All four are multi-position (postings can be in any commodity) and hidden from the account list.

## Sign convention (Beancount-style)

Stored split **quantities** follow the Beancount convention:

- Asset splits: positive = increase.
- Liability splits: negative = increase (more owed = more negative).
- Equity splits: negative = increase (initial wealth is booked as a negative equity quantity).
- For every commodity in a transaction, the sum of split weights is zero. See [transactions.md](transactions.md#conceptual-model) for the weight-balance rule.

Consequences:

- Net worth = `SUM(asset balances) + SUM(liability balances)` in display currency, with no type-conditional arithmetic.
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

- **Cash, Credit Card, Loan, Other Liability, Other Asset:** require an initial **amount** and **as-of date**. The user enters a positive number; the server applies sign conventions. Default as-of date is today; the server accepts dates up to one day past UTC today to tolerate timezone drift between client and server (Homestead supports cross-timezone households). No "skip" affordance — users who don't know the current balance can enter 0; no opening-balance transaction is recorded in that case, and they can add one later via *Add opening balance* on the account detail page.
- **Investment:** no initial balance field at creation. Positions are added afterward via the *Add position* action on the account detail page.

**Post-submit:** navigate to the account detail page.

## Initial balance mechanics

Creating a non-Investment account with an initial balance emits one opening-balance transaction booked against `Equity:Opening Balances`. Adding a position on an Investment account emits the same shape per commodity.

**Cash account, $1,000 CAD on 2026-04-23:**

```
Transaction: Type=OpeningBalance, OccurredAt=2026-04-23T00:00:00Z
  Split 1: Account=<cash>,      Commodity=CAD, Quantity=+1000
  Split 2: Account=<Equity:OB>, Commodity=CAD, Quantity=-1000
```

Weight balance: CAD = +1000 − 1000 = 0 ✓. No annotation — both splits are in the same commodity.

**Credit Card, $500 CAD owed on 2026-04-23** — the server flips the sign because Kind is a liability:

```
Transaction: Type=OpeningBalance, OccurredAt=2026-04-23T00:00:00Z
  Split 1: Account=<credit card>, Commodity=CAD, Quantity=-500
  Split 2: Account=<Equity:OB>,   Commodity=CAD, Quantity=+500
```

Weight balance: CAD = -500 + 500 = 0 ✓.

**Investment *Add position* — 100 shares of VEQT @ $35.12 CAD on 2026-04-23** (investment shapes default to `12:00 UTC` on the chosen date — see [commodities.md](commodities.md#price-time-semantics)):

```
Transaction: Type=OpeningBalance, OccurredAt=2026-04-23T12:00:00Z
  Split 1: Account=<investment>, Commodity=VEQT, Quantity=+100 @ 35.12 CAD
  Split 2: Account=<Equity:OB>,  Commodity=CAD,  Quantity=-3512
```

Weight balance: CAD = (+100 × 35.12) + (-3512) = 0 ✓. The security leg's annotation bridges into CAD; the equity leg sits in CAD natively. Also emits a `CommodityPrice` row: `(VEQT, CAD, 2026-04-23T12:00:00Z, 35.12)`.

**Investment *Add position* — 100 shares of VEQT on 2026-04-23 without a per-unit price** (unpriced — the user asserts holdings but hasn't pinned down historical value):

```
Transaction: Type=OpeningBalance, OccurredAt=2026-04-23T12:00:00Z
  Split 1: Account=<investment>, Commodity=VEQT, Quantity=+100
  Split 2: Account=<Equity:OB>,  Commodity=VEQT, Quantity=-100
```

Weight balance: VEQT = +100 − 100 = 0 ✓. Both splits sit in VEQT; the transaction balances per-commodity and no price row is emitted. The holding renders via carry-forward at net-worth time (or as *unpriced* if no price ever resolves — see [commodities.md](commodities.md#missing-price-ux)).

**Investment *Add position* — $5,000 USD cash on 2026-04-23** (foreign-currency cash in a CAD-reporting account):

```
Transaction: Type=OpeningBalance, OccurredAt=2026-04-23T12:00:00Z
  Split 1: Account=<investment>, Commodity=USD, Quantity=+5000
  Split 2: Account=<Equity:OB>,  Commodity=USD, Quantity=-5000
```

Weight balance: USD = +5000 − 5000 = 0 ✓. Cash opening balances — even foreign-currency cash in a differently-reporting Investment account — always balance per-commodity: no annotation, no price row.

**Invariant: one opening-balance transaction per `(account, commodity)` pair.** *Add position* hides commodities the account already holds, so it only ever creates a new position; to change an existing one, use *Edit opening balance* from the Positions tab. The backend also rejects a duplicate `(account, commodity)` opening balance as a safeguard.

## Editing opening balances

Opening-balance transactions are edited only via dedicated per-account (non-Investment) or per-position (Investment) affordances, separate from the New-transaction shape editors.

Rules:

- **Amount / quantity** — always editable.
- **Price per unit** (securities only) — editable; updates the associated `CommodityPrice` row on the same date.
- **As-of date** — editable only if it remains the earliest transaction for its `(account, commodity)` pair. Moving it past later transactions is blocked with a clear error.
- **Delete** — always allowed. Behaves like editing the opening balance to zero, then removing the now-empty entry. Removing the opening balance retroactively shifts the account's running balance by the deleted quantity; any downstream balance entries on that `(account, commodity)` automatically re-reconcile, with the difference absorbed into `Equity:Adjustments` (the same mechanism that handles any other transaction edit/delete).
- **Re-add after delete** — Investment: use *Add position* again. Non-Investment: the detail page exposes an *Add opening balance* action when none exists.

## Add position (Investment only)

Right-side sheet: commodity picker · quantity · as-of date. If the commodity is a security, the sheet additionally requires a price-per-unit and emits a `CommodityPrice` row. Commodities the account already holds are hidden from the picker, so the sheet only adds new positions; an existing position is changed via *Edit opening balance* on the Positions tab.

## Account list

Route: `/finances/accounts`.

- Header: title + *New account* action. When no display currency is configured, a banner links to Settings; when no accounts exist, an empty state prompts the first one.
- A flat, sortable list — **no Kind grouping, summary card, or group totals**. System accounts are hidden. Net worth (the household total and asset / liability breakdown) lives on the Reports page (`/finances/reports`), sourced from the net-worth time-series rather than rolled up here.
- Two view modes, toggled in the toolbar and remembered as a user preference (cards on mobile, table on desktop by default):
  - **Table** — columns: name (institution beneath) · type · latest transaction · balance in display currency · row-actions menu. Headers sort.
  - **Cards** — name, `institution · type` subtitle, balance, latest transaction date.
- Sort (toolbar): by name, type, latest transaction, or total balance, ascending or descending. Balances render in the display currency only (no native amount); an account whose holdings are entirely unpriced shows *Unpriced* / *—*.
- Archived accounts are hidden by default; a single global *Show archived accounts (N)* toggle in the Filter popover reveals them.
- Row actions (kebab): Edit, Archive / Unarchive, Delete. The Delete item is always present but renders disabled when the account has any transactions.

## Account detail

Route: `/finances/accounts/:id`.

**Header:** breadcrumb · name · kind badge · institution · current balance in display currency, with native amount beside it where different.

**Primary actions** (non-archived accounts):

- **New Transaction** — a dropdown of the transaction shapes allowed for this Kind. A base group always offers **Transfer** and **Record balance**. For Investment accounts the menu adds a separated, labelled **Trade** group with **Buy**, **Sell**, **Dividend**, and **Reinvest dividend** — Buy/Sell open the trade sheet in create mode (with the chosen side); Dividend opens the dividend sheet; Reinvest dividend opens the DRIP sheet.
- **Add position** — Investment accounts only.
- **Add opening balance** — non-Investment accounts only, and only when the account has no opening balance yet.

**Layout.** The detail page is a stack of cards, not a tabbed view:

- **Positions** (Investment accounts only) — a card with a table: commodity · quantity · display value · share of account. Quantity is the native amount (there is no separate native-value column). Each row has an *Edit opening balance* action when one exists. The Positions card also surfaces the set of held commodities whose kind is a currency; the detail page uses this signal to decide whether the Trade (Buy/Sell) sheet reveals its funding-currency picker — it stays hidden unless the account holds cash in a currency other than its reporting currency.
- **Activity** — a unified chronological list of every transaction affecting this account, including opening balances, balance entries, transfers, and — on Investment accounts — trades (Buy / Sell), dividends, and dividend reinvestments (DRIPs), differentiated by icon / label rather than by separation. Trades park both legs on this account, so the row disambiguates them by annotation: it shows the signed cash movement (Buy negative, Sell positive) in the funding currency, with a `<quantity> @ <unit price>` subtitle drawn from the priced security leg.

Reporting currency, institution detail, and created / updated timestamps are not surfaced on the detail page in v1 (there is no Info section).

Archived accounts render read-only. Primary actions disappear; Edit and Unarchive remain.

## Edit / Archive / Delete

**Edit** (right-side sheet):
- Editable fields: Name, Institution, Archived flag.
- Immutable fields: Kind, Reporting currency — shown but disabled.

**Archive / Unarchive:** toggles the `IsArchived` flag. Archived accounts are read-only; balance-entry, transaction, and add-position actions are not offered.

**Delete:** allowed only when the account has zero transactions (opening balance included). Otherwise the Delete menu item renders disabled, leaving Archive as the available action.

## Privacy mask

A user-toggleable mask that hides every numeric value across the Finances module, for use in shared / over-the-shoulder settings.

**Toggle.** Eye / eye-off icon button in the Finances page header (lives in the layout component, so it appears on every Finances route — Accounts list and detail, Positions tab, Reports, Commodities). Pressing toggles between masked and revealed.

**Scope.** All numeric surfaces in Finances render through a single helper that respects the toggle: account list balances, group / net-worth totals, account-detail header balance, Activity row amounts, Positions tab (quantity and display value), the net-worth chart, and reports. The Positions tab's **share** column is intentionally **not** masked — a relative percentage of the account total leaks no absolute magnitude. Likewise, the **derived read-outs inside the transaction-entry sheets** — the Buy/Sell **Total** and the DRIP **reinvestment price** — are intentionally **not** masked: each is computed entirely from input fields the user is actively typing in plain text, so masking them adds no privacy (an onlooker can already compute them from the visible inputs) while breaking the form's live-feedback loop. Non-numeric data (names, dates, kinds, currency codes) is likewise never masked.

**Mask format.** The mask is **value-independent**: rather than bulleting the real number's own digits, the helper formats a constant placeholder (`0`) at the commodity's display precision and replaces each digit with U+2022 (`•`). Because `0` has a single integer digit and never triggers grouping, the masked output is a fixed shape — one integer bullet plus `precision` fraction bullets — that reveals neither magnitude nor sign. The currency symbol, decimal mark (`.`), and the trailing security symbol for non-currency commodities are preserved; **grouping marks are never emitted** (there is nothing to group), and sign characters are stripped so the output is constant-width and does not reveal whether the value is negative. E.g. `$1,234.56` → `$•.••`, `100 VEQT` → `•.•••• VEQT` (VEQT at display precision 4).

**Persistence.** The masked/revealed state survives across sessions (`localStorage`).

**Auto-remask.** Once revealed, the mask re-engages automatically after **5 minutes of inactivity** (no pointer / wheel / touch / keyboard activity). A "revealed-at" anchor is refreshed in `localStorage` on activity (throttled to once per 30s) and on page hide; on cold start, if the anchor is missing or older than the timeout, the mask is re-applied before any value is rendered. Switching tabs counts as idle — returning to the tab after >5 min hidden re-masks immediately.

**Default.** Masked. New sessions and stale resumes start in the masked state; the user must explicitly press the toggle to reveal.

### Font rendering note: the leading bullet sits higher

A consequence of using `•` as the mask glyph with the Inter font: in strings like `CA$•.••`, the **first** bullet renders visibly higher than the rest. This is not a bug — Inter ships a case-sensitive form of U+2022 that shifts up to cap-height when shaped adjacent to all-caps text or `$`. Only the bullet touching `CA$` triggers the shift; subsequent bullets (flanked by `.` or other bullets) stay at the default x-height. The masking code emits identical U+2022 characters; the visual offset is introduced by the font's OpenType shaping. Acceptable as-is in v1. If the inconsistency ever needs to be removed, options are (a) switch the mask glyph to one without case-sensitive forms in Inter (e.g. `●` U+25CF), or (b) disable the case feature on masked spans via `font-feature-settings` — both untested.
