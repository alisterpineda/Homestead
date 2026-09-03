# Homestead — Product Design

Product-level requirements and design decisions for Homestead's modules: what each
module should do, the user-facing concepts it exposes, the decisions that shape it,
and what is deliberately deferred. This is the *what*, not the *how* — code
structure, framework choices, and persistence/auth plumbing live in the repo's
CLAUDE.md files (`/CLAUDE.md`, `Homestead.WebApi/CLAUDE.md`,
`Homestead.WebApp/CLAUDE.md`).

> The cross-cutting product principles and the agent routing table (which doc to
> consult for which area of the codebase) live in the `product-design` skill at
> [`.claude/skills/product-design/SKILL.md`](../../.claude/skills/product-design/SKILL.md).
> The per-module deep dives below are the source of truth for each module's design.

## Modules

| Module | User-facing? | Docs |
|---|---|---|
| **Documents** | Yes | [overview](documents/overview.md) · [search](documents/search.md) · [tags](documents/tags.md) · [versions](documents/versions.md) |
| **Finances** | Yes | [overview](finances/overview.md) · [accounts](finances/accounts.md) · [commodities](finances/commodities.md) · [transactions](finances/transactions.md) · [payees](finances/payees.md) · [valuation](finances/valuation.md) |
| **Inventory** | Yes | [overview](inventory/overview.md) · [locations](inventory/locations.md) · [stock](inventory/stock.md) · [tags](inventory/tags.md) · [brands](inventory/brands.md) · [activity](inventory/activity.md) |
| **Processing** | Read-only status surface | [overview](processing/overview.md) |
| **Admin**, **Identity**, **Status** | Admin-only · indirect · none | No product doc (infrastructure) — see the CLAUDE.md files. |

## Cross-cutting

- [shared/table-ux.md](shared/table-ux.md) — product/UX rules for every data-table screen (toolbars, the unified-popover filter pattern, selection state, responsive behaviour). Applies across modules.
- [shared/universal-search.md](shared/universal-search.md) — the global command palette (⌘K): search-only, grouped per-source (no global ranking), and the `ISearchSource` / presenter seam that makes new modules additive. v1 ships Inventory items.

## Maintenance

When something listed under a doc's *Future-compatibility* / *Out of scope* section ships, move it into the in-scope description **in the same change**. Stale "future" entries for already-shipped features (with the in-scope section updated but the future section forgotten) were the most common form of doc drift found in the 2026-06 docs-vs-code audit.
