# Self-QA reference — Finances (`/finances`, FinancesLayoutComponent; index → accounts)

Sidebar order: Accounts / Commodities / Categories / Payees / Reports / Settings.

| Route | Component |
|---|---|
| `/finances/accounts` | AccountListPageComponent — the row's "Account actions" menu has only Edit / Archive / Delete; **Import statement** is a header button on the account detail page (`/finances/accounts/:id`) → upload/pick a statement doc → AI parse → review page |
| `/finances/accounts/:id` | AccountDetailPageComponent (4 account sheets — linkedSignal seeding) |
| `/finances/accounts/:id/statement-import/:jobId` | StatementImportReviewPageComponent — payee-primary TanStack table: per-line payee picker with **learned / AI-ghost / you-set** provenance (✨ Accept / Create & use), read-only default-category chip, "Accept all suggestions (N)" + Suggestions filter, and an **unreviewed-suggestions warning dialog** on Import (Review them / Apply all & import). Reached only via an account's Import statement flow — **requires AI, which is off by default**. |
| `/finances/commodities` | CommodityListPageComponent |
| `/finances/commodities/:id` | CommodityDetailPageComponent |
| `/finances/categories` | CategoryListPageComponent — nested income/expense category tree; create dialog + edit sheet with parent picker; two read-only "Uncategorised Expense/Income" system nodes pinned last; per-node Edit/Delete menu |
| `/finances/payees` | PayeeListComponent — see below |
| `/finances/reports` | ReportsPageComponent |
| `/finances/settings/general` | GeneralSettingsComponent |
| `/finances/settings/price-sources` | PriceSourceListComponent |
| `/finances/settings/price-sources/:source` | PriceSourceDetailComponent |

- The Expense/Income flow sheet has a direction-filtered **Category (optional)** picker (leaf name →
  activity subtitle).
- Payees: row ⋮ menu → Edit / **Merge into…** / Delete. Merge dialog has a target payee picker. Edit
  dialog has a default-category picker plus a **"Learned from these descriptions"** section (learned
  statement-import mappings, each with a × remove) and a rename→409→**Merge this payee into it**
  handoff. The payee picker shows a **"Did you mean {existing}?"** dedupe nudge for near-duplicates.
