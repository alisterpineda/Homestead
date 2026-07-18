---
name: product-design
# Editor note: keep this description concise and generic. Do NOT expand it with
# module-specific details, lists, or examples when adding content to the body or
# new module references — the description is for skill-matching, not summarising
# the skill's contents. Prefer rewriting over appending. Module specifics belong
# in the body and references, not here.
description: Product-level requirements and design decisions for Homestead's modules — what each should do and the constraints that shape it. Consult before designing, implementing, modifying, or reviewing module functionality, API contracts, schemas, UX flows, or cross-module behaviour. The top-level file routes to per-module references — load the module overview plus any deep-dive matching the area you're touching.
---

# Homestead — Product Design

This skill captures Homestead's product-level requirements: what each module should do, the user-facing concepts it exposes, the design decisions that shape it, and what is deliberately deferred. It is the *what*, not the *how*. Code structure, framework choices, and persistence/auth plumbing live in the repo's CLAUDE.md files (`/CLAUDE.md`, `Homestead.WebApi/CLAUDE.md`, `Homestead.WebApp/CLAUDE.md`) — defer to those for implementation patterns.

The per-module references this skill routes to live in the repository's `docs/product-design/` directory, so they double as browsable project documentation. This file is the routing table and the cross-cutting principles; the deep-dive content is in `docs/`.

## What Homestead is

Homestead is a self-hosted suite for a single household. Each user signs in once via OIDC and moves between modules through an in-app switcher; there is no separate per-module login. The user-facing modules are Documents (managing files, tags, and linked parties), Finances (tracking household net worth), and Inventory (tracking household possessions and consumable supplies) — plus a small set of cross-cutting infrastructure modules. Inventory is the newest; its design is captured in the references below. New modules will be added under the same shell and should follow the same product-doc shape used here.

## Modules at a glance

| Module | Purpose | User-facing? | Product doc |
|---|---|---|---|
| **Documents** | Upload, tag, search, version, and organise household files; link them to people/entities (parties); soft-delete via trash. | Yes | [docs/product-design/documents/overview.md](../../../docs/product-design/documents/overview.md) |
| **Finances** | Track household net worth across cash, credit, debt, and investment accounts in any currency; reconcile against monthly statements. | Yes | [docs/product-design/finances/overview.md](../../../docs/product-design/finances/overview.md) |
| **Inventory** | Track household possessions and where they physically live (areas + nesting containers, derived location); manage consumable supplies, per-location stock, low-stock signals, and usage over time. | Yes | [docs/product-design/inventory/overview.md](../../../docs/product-design/inventory/overview.md) |
| **Admin** | Manage users, groups, text-extraction engine configuration, AI configuration status (read-only — the AI integration is set up via environment/secrets, not in-app CRUD), processing schedules (recurring cron jobs), and system settings (runtime log-level overrides). | Yes (admin role only) | — no product doc; infrastructure / configuration surface |
| **Identity** | OIDC sign-in, sign-out, and current-user info. | Indirect (every page passes through the auth flow) | — no product doc; infrastructure |
| **Processing** | Background-job queue for OCR, reprocessing, ZIP downloads, and other async work initiated by other modules. | Read-only status surface | [docs/product-design/processing/overview.md](../../../docs/product-design/processing/overview.md) — infrastructure / architecture |
| **Status** | Health/liveness probe for deployment orchestration. | No | — no product doc; infrastructure |

## When to load which reference

- **Working in `Homestead.WebApi/Modules/Finances/`, `Homestead.WebApi/Domain/Finances/`, or `Homestead.WebApp/src/app/modules/finances/`** — load [docs/product-design/finances/overview.md](../../../docs/product-design/finances/overview.md) plus the relevant deep-dive:
  - [accounts.md](../../../docs/product-design/finances/accounts.md) for account kinds, creation, opening balances, archive/delete, list/detail UI.
  - [commodities.md](../../../docs/product-design/finances/commodities.md) for currency/security modelling, prices, carry-forward, FX chaining.
  - [transactions.md](../../../docs/product-design/finances/transactions.md) for transaction shapes, splits, balance entries, validation, edit/delete, and statement import.
  - [payees.md](../../../docs/product-design/finances/payees.md) for payees, the picker, default-category autofill, and the statement-import learning/suggestion/merge subsystem.
  - [valuation.md](../../../docs/product-design/finances/valuation.md) for the read-side API contract — balances, positions, display-currency rendering, `null` vs `0` rules.
- **Working in `Homestead.WebApi/Modules/Documents/`, `Homestead.WebApi/Domain/Documents/`, or `Homestead.WebApp/src/app/modules/documents/`** — load [docs/product-design/documents/overview.md](../../../docs/product-design/documents/overview.md) plus the relevant deep-dive:
  - [tags.md](../../../docs/product-design/documents/tags.md) for the path-delimited hierarchy, colour inheritance, descendant cascades on edit/delete, search expansion through child tags, and the role of tags in ZIP downloads.
  - [versions.md](../../../docs/product-design/documents/versions.md) for the document/version split, the asynchronous upload pipeline, the silent-on-upload / loud-on-reprocess text-extraction contract, single + bulk reprocess, and the household-wide extraction-engine setting.
  - [search.md](../../../docs/product-design/documents/search.md) for the five-mode OR query (name / extracted-text / tag-name / party-name ILIKE + FTS), the weighted `tsvector` (name = A, latest extracted text = B, summary = C), prefix expansion, the filter taxonomy, and the "search hides sort" rule.
- **Working in `Homestead.WebApi/Modules/Inventory/`, `Homestead.WebApi/Domain/Inventory/`, or `Homestead.WebApp/src/app/modules/inventory/`** — load [docs/product-design/inventory/overview.md](../../../docs/product-design/inventory/overview.md) plus the relevant deep-dive:
  - [locations.md](../../../docs/product-design/inventory/locations.md) for areas, the containment tree (containers as a role, not a type), derived/resolved location, move-with-contents, the Unplaced state, containment typing/cycle rules, and the non-fungible item lifecycle (status, archive-vs-delete, container emptiness).
  - [stock.md](../../../docs/product-design/inventory/stock.md) for fungibility, stock entries (one-per-location merge, zero-row removal), the append-only movement ledger (structural direction + `Flow`/`Adjustment`/`Conversion` kind) and the usage query, optional restock thresholds, the track-one-out / collapse conversions, the single-service write funnel, and quantity concurrency (atomic decrement, no `xmin`).
  - [tags.md](../../../docs/product-design/inventory/tags.md) for the hierarchical classification tags (path identity, `Color`/`EffectiveColor` cascade), the descendant-expanding items-list filter facet, inline path-based find-or-create on the item forms, the dedicated `/inventory/tags` CRUD page, and destructive delete (un-tag + re-parent, no in-use block, no ACL guards). Tags replace the deferred Category axis and are not wired into item search / ⌘K in v1.
  - [brands.md](../../../docs/product-design/inventory/brands.md) for the canonical manufacturer lookup (flat shared row, single-valued `Item.BrandId` FK), entry-time find-or-create via the brand picker, the dedicated `/inventory/brands` management page (rename / delete / merge, ⋮-menu), delete-blocks-when-in-use (contrast Tags' destructive delete), the merge FK-reassign + audit semantics, and the rename-409 → merge handoff. Brand is a facet + detail chip, not wired into item search / ⌘K in v1.
- **Touching background jobs** — load [docs/product-design/processing/overview.md](../../../docs/product-design/processing/overview.md) whenever you are adding a new job type, a new job handler, a new parent/child fan-out, changing notification semantics, or working anywhere in `Homestead.WebApi/Processing/`, `Homestead.WebApi/Modules/*/Processing/`, `Homestead.WebApi/Domain/Processing/`, or the SignalR-backed job UI. Also load it from a feature reference (e.g., Documents) when the feature you are designing relies on async processing.
- **Working in other infrastructure modules (Admin, Identity, Status)** — there is no product doc. Defer to the CLAUDE.md files in `Homestead.WebApi/` and `Homestead.WebApp/` for the architectural treatment.
- **Designing or modifying any data-table screen (list pages, toolbars, filters, bulk-action UI)** — also load [docs/product-design/shared/table-ux.md](../../../docs/product-design/shared/table-ux.md). Covers toolbar zones, the unified-popover filter pattern (with `HlmSheet` on mobile), selection-state behaviour, and responsive rules. Applies cross-module.

When work spans multiple modules (e.g., a Documents change that triggers a Processing job, or an audit-log change that affects both Documents and Finances), load both module overviews so cross-cutting principles below can be applied consistently.

## Cross-cutting product principles

Apply these unless a module reference explicitly overrides them:

- **Self-hosted, single household.** No multi-tenant story in v1. Permissions, sharing, and household-level scoping are deferred — code paths must not bake assumptions that block them later.
- **OIDC is the only sign-in method.** No local username/password. User provisioning happens automatically on first OIDC callback.
- **Field-level audit log under the hood, mostly not user-visible.** Every mutation writes to `auditing.audit_logs` via `IAuditLogger`. Documents projects entries into its activity stream as human-readable summaries; no module surfaces per-field diffs (before/after values) in v1. The back-end log exists so a richer history view can be added later without backfill.
- **No soft delete and no undo in v1.** Documents has a trash flow specifically for restorable deletion of files; everywhere else, delete is destructive from the user's perspective. (The audit log retains the diff, but it is not surfaced.)
- **Modules are independent surfaces, loosely referenced.** Users navigate via an app-switcher; each module owns its own sidebar, routing, and state. Where one module cites another's data, the reference is **ID-first**: reads go through the owning module's access-filtered query service, and no navigation property crosses module schemas. Whether the ID carries a real FK depends on lifecycle: a reference whose row should **die with its target** may use a cross-schema FK with cascade (e.g., transaction-document links cascade away with the document), while provenance-style references that must **outlive** their target stay FK-less (e.g., audit logs). See [finances/transactions.md § Linked documents](../../../docs/product-design/finances/transactions.md#linked-documents) for the canonical example of both considerations.
- **Display-time conversions, not write-time rewrites.** When the user changes a preference that affects how data is rendered (Finances display currency, theme, view mode), nothing in storage is rewritten — the change re-renders on read.
