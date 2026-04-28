---
name: product-design
description: Product-level requirements and design decisions for Homestead — a self-hosted single-household suite spanning the Documents and Finances modules. Consult this before designing, implementing, modifying, or reviewing any user-facing functionality, API contract, schema, or UX flow in those modules. The top-level file routes to per-module references; load the module overview plus any deep-dive that matches the area you are touching. Use this even when the work seems adjacent — naming conventions, scope boundaries, sign conventions, audit behaviour, and forward-compatibility guarantees should all be reconciled against the module reference first.
---

# Homestead — Product Design

This skill captures Homestead's product-level requirements: what each module should do, the user-facing concepts it exposes, the design decisions that shape it, and what is deliberately deferred. It is the *what*, not the *how*. Code structure, framework choices, and persistence/auth plumbing live in the repo's CLAUDE.md files (`/CLAUDE.md`, `Homestead.WebApi/CLAUDE.md`, `Homestead.WebApp/CLAUDE.md`) — defer to those for implementation patterns.

## What Homestead is

Homestead is a self-hosted suite for a single household. Each user signs in once via OIDC and moves between modules through an in-app switcher; there is no separate per-module login. v1 ships two user-facing modules — Documents (managing files, tags, and linked parties) and Finances (tracking household net worth) — plus a small set of cross-cutting infrastructure modules. New modules will be added under the same shell and should follow the same product-doc shape used here.

## Modules at a glance

| Module | Purpose | User-facing? | Product doc |
|---|---|---|---|
| **Documents** | Upload, tag, search, version, and organise household files; link them to people/entities (parties); soft-delete via trash. | Yes | [references/documents/overview.md](references/documents/overview.md) |
| **Finances** | Track household net worth across cash, credit, debt, and investment accounts in any currency; reconcile against monthly statements. | Yes | [references/finances/overview.md](references/finances/overview.md) |
| **Admin** | Manage users, groups, and text-extraction engine configuration. | Yes (admin role only) | — no product doc; infrastructure / configuration surface |
| **Identity** | OIDC sign-in, sign-out, and current-user info. | Indirect (every page passes through the auth flow) | — no product doc; infrastructure |
| **Processing** | Background-job queue visibility for OCR, reprocessing, and other async work initiated by other modules. | Read-only status surface | — no product doc; infrastructure |
| **Status** | Health/liveness probe for deployment orchestration. | No | — no product doc; infrastructure |

## When to load which reference

- **Working in `Homestead.WebApi/Modules/Finances/`, `Homestead.WebApi/Domain/Finances/`, or `Homestead.WebApp/src/app/modules/finances/`** — load [references/finances/overview.md](references/finances/overview.md) plus the relevant deep-dive:
  - [accounts.md](references/finances/accounts.md) for account kinds, creation, opening balances, archive/delete, list/detail UI.
  - [commodities.md](references/finances/commodities.md) for currency/security modelling, prices, carry-forward, FX chaining.
  - [transactions.md](references/finances/transactions.md) for transaction shapes, splits, balance entries, validation, edit/delete.
  - [valuation.md](references/finances/valuation.md) for the read-side API contract — balances, positions, display-currency rendering, `null` vs `0` rules.
- **Working in `Homestead.WebApi/Modules/Documents/`, `Homestead.WebApi/Domain/Documents/`, or `Homestead.WebApp/src/app/modules/documents/`** — load [references/documents/overview.md](references/documents/overview.md). (Currently a stub — flag missing detail rather than inferring intent from code.)
- **Working in any infrastructure module (Admin, Identity, Processing, Status)** — there is no product doc. Defer to the CLAUDE.md files in `Homestead.WebApi/` and `Homestead.WebApp/` for the architectural treatment.

When work spans multiple modules (e.g., a Documents change that triggers a Processing job, or an audit-log change that affects both Documents and Finances), load both module overviews so cross-cutting principles below can be applied consistently.

## Cross-cutting product principles

Apply these unless a module reference explicitly overrides them:

- **Self-hosted, single household.** No multi-tenant story in v1. Permissions, sharing, and household-level scoping are deferred — code paths must not bake assumptions that block them later.
- **OIDC is the only sign-in method.** No local username/password. User provisioning happens automatically on first OIDC callback.
- **Field-level audit log under the hood, not user-visible.** Every mutation writes to `auditing.audit_logs` via `IAuditLogger`. No user-facing change-history UI in v1; the back-end log exists so it can power one later without backfill.
- **No soft delete and no undo in v1.** Documents has a trash flow specifically for restorable deletion of files; everywhere else, delete is destructive from the user's perspective. (The audit log retains the diff, but it is not surfaced.)
- **Modules are independent surfaces.** Users navigate via an app-switcher; each module owns its own sidebar, routing, and state. Cross-module references (e.g., a Finances transaction citing a Documents file) are not a v1 concern.
- **Display-time conversions, not write-time rewrites.** When the user changes a preference that affects how data is rendered (Finances display currency, theme, view mode), nothing in storage is rewritten — the change re-renders on read.
