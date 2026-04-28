# Documents — Requirements

> **Stub.** This document mirrors the structure of [../finances/overview.md](../finances/overview.md) so the Documents module has a consistent product-level shape, but the sections below are placeholders. Treat absent content as **unwritten**, not as **out of scope**. When working in the Documents module, consult the running code and the user before assuming intent on anything not yet captured here.

This document describes the Documents module within Homestead: what it is, what it does, what it does not do, and the design decisions that shape it. It is product-level, not implementation-level — see the repo's CLAUDE.md files for code structure and framework choices.

Documents sits alongside Finances and the cross-cutting infrastructure modules listed in the top-level [SKILL.md](../../SKILL.md).

## Purpose

> TODO: One-paragraph statement of what the Documents module exists to do for the household. Establish the primary outcome (e.g., a durable, searchable archive of household documents linked to the people and entities they relate to) and what the module is *not* (e.g., not a full DMS, not a workflow tool, not a signing platform).

## In scope for v1

> TODO: Enumerate the user-facing capabilities that ship in v1. The current implementation suggests this set, but the *intent* needs to be confirmed before treating any of these as load-bearing:
>
> - File upload, with thumbnail and OCR/text-extraction processing.
> - Document versioning — newer uploads supersede older versions while preserving history.
> - Hierarchical tags with colour and path-based grouping.
> - Linked parties (people / entities the document pertains to).
> - Comments / activity per document.
> - Trash with restore and permanent-delete.
> - Bulk operations: bulk delete, bulk metadata update, bulk reprocess.
> - PostgreSQL full-text search across document name (weighted A) and extracted text (weighted B).
> - ZIP download organised by tag path.

## Out of scope for v1

> TODO: Make the boundary explicit. Likely candidates based on what the code does *not* do today: in-document collaborative editing, signing/workflow approval, OCR engine training, automated tagging, multi-tenant or per-user document scoping, version diffs, document relationships beyond the party-link, configurable retention policies. Confirm and tighten this list.

## User-facing concepts

### Document

> TODO: What a Document is from the user's perspective. Identity (name, document date, expiry date, notes), tags, parties, the latest version vs the version history, audit fields, soft-delete via trash.

### Document version

> TODO: Each upload produces a Version with its own file, extracted text, and metadata. Define which fields are version-scoped vs document-scoped, when reprocessing happens, and what the user sees when they upload a new version.

### Tag

> TODO: Hierarchical tags with colour. Define the path model (parent-child? slash-delimited path?), how tags participate in search, and the rules around create / edit / delete (especially deletes that affect tagged documents).

### Party

> TODO: Linked entities — people, organisations, anything the document is *about*. Define the relationship cardinality (a document may have many parties; a party has many documents), party identity (name, contact info?), and edit/delete rules.

### Comment / Activity

> TODO: Free-text comments and the activity stream that captures mutations and uploads. Define what is auto-generated vs user-authored, ordering, edit/delete rules.

### Trash

> TODO: Soft-delete with restore. Unlike Finances (which has no soft delete), Documents needs trash because file deletion is high-cost to reverse and users frequently delete by mistake. Define the lifecycle (delete → trash → restore or permanent-delete), retention policy, and any auto-purge rules.

## Design principles

> TODO: The non-obvious decisions that shape Documents. Likely candidates to capture:
>
> - Text extraction is asynchronous and best-effort — search results lag uploads by a job-queue cycle.
> - Search is FTS-weighted, not exact — ranking matters more than membership.
> - Tags are organisational, not authoritative — parties carry the semantic linkage.
> - Versioning is monotonic — older versions are read-only references; mutations always produce a new version.
> - The audit log is the source of truth for "what happened"; the activity stream is a curated user-facing view of it.

Confirm each before relying on it.

## Distinctive UX decisions worth preserving

> TODO: The choices that make Documents feel like Documents. Examples that may be true today: search is the primary entry point on the list page, not a filter; bulk operations live as a mode toggle rather than a separate page; the trash is opt-in (users have to navigate to it) so the default list shows only live documents; tag colours are user-controlled and reflected in badges everywhere a tag appears.

## Future-compatibility

> TODO: What must remain addable later without rewriting history. Candidates:
>
> - Document relationships beyond the party-link (e.g., supersedes / supersedes-by, derived-from).
> - Configurable retention and expiry-driven workflows.
> - Multi-user / household sharing and per-document permissions.
> - External provider integrations (cloud-storage import, scanner uploads).
> - Inline preview / annotation.
> - User-facing change-history UI driven by the existing audit log.

None of these should require re-migrating historical document data.
