# Warranties — design

Companion to [overview.md](overview.md). This document covers **warranties** on durable items — the small child record a household keeps so "is this still covered?" has an answer, the **derived status** computed from those records, and the three surfaces that expose it: the Warranties section on the durable detail page, the Warranty facet on the Items list, and the "Warranty expiring" tile in Browse's needs-attention region. It shares the note composer's interaction pattern from [activity.md](activity.md) and diverges from the note *rules* in two places that matter — deletion and audit projection — which are called out explicitly.

Proof of coverage (the receipt, the warranty card, the manual page) is **not** here: it arrives with the future generic item → Documents link. Nothing in this design references the Documents module.

## Purpose

Household durables come with coverage, and the coverage is the thing people forget. A dishwasher has one year on parts and labour and five on the motor; a laptop has the manufacturer's year plus the retailer's three-year extended plan; a roof has a lifetime manufacturer warranty on the shingles. The question a household actually asks is narrow and time-sensitive: *is this still under warranty, and is anything about to lapse?* — asked exactly when something breaks, or a month before the extended plan runs out and the decision to renew is still available.

Inventory already answers *where is it* and *what has happened to it*. Warranties answer *is it still covered*. That is the whole scope. This is **not** a claims tracker (there is no claim state, no ticket, no RMA number), **not** a scheduler (nothing here emails, notifies, or creates a task — a lapse surfaces only where the user is already looking), and **not** a document store (see *Future-compatibility*).

## Conceptual model

A durable has **zero or more warranties**, each a row in its own table (`inventory.item_warranties`) keyed by `Item.Id`. Two facts shape everything below:

- **Multiple rows per item is the normal case, not the edge case.** The manufacturer-full + manufacturer-motor and manufacturer + retailer-extended pairs are the two most common household shapes. A design that allowed one warranty per item would force the user to pick which coverage to record and throw the other away.
- **The status the user reads is derived from the whole set, never stored.** One item, one status; it is recomputed on every read from the rows as they stand today. Nothing has to be reconciled, nothing goes stale overnight, and no background job exists to expire anything.

Warranties are **not notes** and are not merged into the activity feed as content. A note is free text about what *happened*; a warranty is structured data the system reasons about — it drives a status, a filter facet, and a triage tile. The rule the module already applies to notes decides this: *only values the system reasons about earn a column*. "Warranty registered" as a note is still a fine remark; the coverage itself is a row.

## The warranty

A warranty is a small, deliberately loose record.

| Field | Rule |
|---|---|
| `Id` | uuid PK. |
| `ItemId` | FK to `inventory.items`, **cascade** on delete, with **no** delete gate (see *Deletion*). Indexed with the expiry: `(ItemId, ExpiresOn)`. |
| `provider` | Who backs the coverage — "Bosch", "Best Buy", "SquareTrade". Free text, max 200, nullable, trimmed. **Null means the manufacturer** (see below). |
| `expiresOn` | The date coverage ends — a plain date, no time or zone. Nullable; **null means no expiry** (see below). No future-or-past validation: back-filling a warranty that already lapsed is a legitimate act of record-keeping. |
| `notes` | One short line distinguishing this row from its siblings — "motor only", "parts and labour", "extended plan #4471". Max 500, nullable, trimmed. Not a paragraph and not a second description field. |
| `CreatedAt`, `UpdatedAt`, `CreatedByUserId`, `UpdatedByUserId` | Standard attribution; `SetNull` on user delete; display only, never access control. |

### Two nulls that carry meaning

Both nullable fields have a **defined meaning when empty**, which is what keeps the record small:

- **`provider` null = the manufacturer.** Rendered as the word **"Manufacturer"**, or as the item's brand when it has one ("Bosch · manufacturer"). The brand is resolved **at display time and never copied into the row** — re-branding an item, or fixing a misspelled brand, must not leave a stale manufacturer name frozen in its warranties. The composer's placeholder is literally "Manufacturer", so leaving the field alone is a deliberate, visible choice rather than an omission.
- **`expiresOn` null = open-ended.** Lifetime coverage, or coverage whose end date the household never knew. This is a real answer, not missing data, so it renders as **"no expiry"** and counts as `Active` forever.

Consequently an **all-null row is valid and meaningful**: a lifetime manufacturer warranty, recorded in one tap. And **multiple null-provider rows on one item are allowed** — the manufacturer's own tiers, distinguished by `notes` ("parts and labour" vs "motor only"). There is no uniqueness rule of any kind; the household, not the schema, decides what counts as a duplicate.

### What is deliberately not on the warranty

- **No kind / category enum** (Manufacturer / Extended / Store). The null-provider convention already carries the one distinction that matters, and a fixed enum would repeat the Category mistake this module corrected once already. Additive later with a default if a behaviour ever depends on it.
- **No start date and no duration.** Coverage that has not started yet is not a household concern, and "one year from purchase" is *computed* at entry time from `AcquiredOn` by the composer's presets — storing both a duration and an end date invites them to disagree.
- **No document FK.** Proof belongs to the future item → Documents link, which will serve receipts, manuals, and warranty cards through one seam rather than one FK per feature.
- **No claim state.** "Claimed", "in RMA", "replaced under warranty" is exactly what the activity feed's notes are for.

### Which items can carry warranties

**Durables only.** A warranty attaches to *a thing*, and a fungible consumable is a count of a type, not a thing — "the AA batteries are under warranty" has no referent. Attempting to add one to a consumable is a business-rule error, enforced in the handler rather than by a check constraint (the rule spans two tables, exactly like the parent-item rule in [locations.md](locations.md)).

Warranties can be added to a durable in **any status**, archived ones included: recording the coverage on something already retired is normal record-keeping, and a lapsed warranty on an appliance you threw out is history worth keeping.

**Components own their own warranties, and nothing rolls up.** The PSU inside a gaming PC carries its own coverage; the assembly's status reflects only the assembly's own rows. Rolling a component's warranty up into its parent would make the parent's status a claim about something the parent is not, and the component is a first-class item with its own detail page where its coverage already reads correctly.

## Derived status

Every durable has exactly one **warranty status**, one of `None`, `Active`, `Expiring`, `Expired`. It is **never stored** — it is computed from the item's rows on every read, against the **server's current UTC date** (a plain date, matching `expiresOn`; a household spanning one time zone gains nothing from per-user local dates, and a stored status would need a nightly job to stay honest).

| Condition | Status |
|---|---|
| The item has no warranty rows | `None` |
| Any row is open-ended (`expiresOn` null) | `Active` |
| Otherwise, the **latest** `expiresOn` is before today | `Expired` |
| Otherwise, the latest `expiresOn` is within the next **30 days** (inclusive of both today and today + 30) | `Expiring` |
| Otherwise | `Active` |

Two properties are worth stating outright because a user will meet them:

- **The latest expiry decides.** The item-level question is "is anything still covering this?", so the longest-lived row wins and an open-ended row wins absolutely. The accepted cost is the **lapsed-tier case**: a dishwasher with a one-year full warranty and a five-year motor warranty reads `Active` for five years, even though the full coverage lapsed in year two. Reporting the item as partly-expired would require a tier taxonomy the record deliberately does not have (see *What is deliberately not on the warranty*), and per-row status is already visible on the detail page, which is where a household checks what specifically is covered. The item-level status is a triage signal, not a coverage certificate.
- **The 30-day window is a constant, not a setting.** One number, no per-item lead time and no household preference, because the tile it feeds is a nudge on a page the user is already on — not a notification with a cost to being wrong. A per-item lead time is additive if a real case ever appears.

**Per-row status** uses the same rules minus the empty case: a row with no expiry is `Active`, one dated before today is `Expired`, one within the window is `Expiring`, anything later is `Active`. A row is never `None`.

## UX

### Where warranties are entered

**On the durable detail page only.** The Warranties section sits after Components/Contents and before Activity, keeping the shared detail-page anatomy (headline answer → details → contents → activity last). There are deliberately **no warranty fields on the create dialog or the edit sheet** — the create dialog is tuned for speed (Name → Location, everything else demoted under "More details") and a repeating child collection is not a form field — and **no row on the quick panel**, which keeps its nothing-stacks rule and its read-and-adjust scope. Warranties are recorded when you sit down with the paperwork, which is a detail-page moment.

### The composer

The section's composer is the note composer's sibling: a single row under the section header, above the list, with the newest row appearing right beneath where it was typed. Left to right it is an **expiry chip**, a **provider input**, an optional **note reveal**, and the **Add** button. Enter in either input submits.

- **The expiry chip is an explicit choice, and Add is disabled until it is made.** The chip opens a menu of **1 / 2 / 3 / 5 years** (computed from the item's `AcquiredOn` when it has one, else from today, and labelled with the resulting date so the user confirms it rather than trusting arithmetic), **No expiry**, and **Pick a date…** for anything else. Past dates are allowed. This is the one place warranties diverge from the note composer's sticky-date convenience, and deliberately: a note's date defaulting to today is a plausible answer, while an expiry date is the *load-bearing* field — a silently defaulted or carried-forward one produces a confidently wrong status. So the chip starts unset, blocks submission until chosen, and **resets to unset after every successful add** rather than staying sticky.
- **Provider is optional and reads as "Manufacturer" when blank** — the placeholder says so.
- **The note field is revealed on demand** by a small ghost button, so the common case (a manufacturer warranty with an expiry) is a two-interaction record: pick the term, press Add.

Editing a row swaps it in place for the same composer in edit mode with Save / Cancel; Escape cancels; only one composer is open at a time, so the header composer is disabled while an edit is in progress. Edit and Delete live behind the row's `⋮` overflow, and Delete confirms.

### Reading a row

A row reads as one line of identity and one of timing: **"Manufacturer · motor only"** over **"until Mar 12, 2036"** (or "no expiry", or "expired Mar 12, 2027"). A status pill appears **only** for `Expiring` and `Expired` — an active warranty is the expected state and a green "Active" pill on every row would be noise — and an expired row's label is muted so a list of coverage reads at a glance as what is live and what has lapsed. Rows sort by expiry ascending with open-ended coverage last, so the thing about to lapse is at the top.

The empty state is "No warranties recorded." Unlike the activity feed's, it is reachable and common: most items will never have a warranty recorded, which is why the section is quiet.

## List facet

The Items list gains a single-choice **Warranty** group in its filter popover: **Any · Active · Expiring soon · Expired · None**, each with a count, following the module's existing popover conventions ([table-ux.md](../shared/table-ux.md)). It filters on the item's derived status, which is already on the row, so it is a client-side narrowing like the other facets. Consumables carry no warranty status and therefore **drop out of the list entirely** whenever the facet is anything but "Any" — the honest behaviour, since "consumables with no warranty" is not a question anyone is asking.

The facet is **deep-linkable** as `?warranty=` with values `any` · `active` · `expiring` · `expired` · `none`, seeded through the same one-shot URL-param mechanism the other triage deep links use. It joins the filter-label summary and resets with the rest.

## Overview tile

Browse's needs-attention region gains a **"Warranty expiring"** tile at its **root scope only**, beside low stock and lent out: an exact count plus a most-urgent-first preview of up to five items with their expiry dates, and a "View all" landing on the Items list at `?warranty=expiring`. It counts **live durables only** — `Active` and `Lent`; an archived item is out of the active picture by definition — whose status is `Expiring` — so an item with an open-ended row is never in it, matching the status rules exactly.

It is deliberately **not** added to the drilled place scope. The place-scoped panels answer "what needs attention *here*", and coverage lapsing is a property of a thing and its paperwork, not of the room it is stored in; adding a fourth panel to every drilled area would cost vertical space on the surface the module most wants to keep fast.

## Deletion

Warranties **cascade** when their item is deleted, with **no delete gate** — the deliberate opposite of notes. Notes are user-authored history and join the gate that makes archive the way to retire something with a story; a warranty is a *fact about a product* that has no value once the product's record is gone. There is no "this item has warranties and cannot be deleted" error, no copy change to the delete confirmation dialogs, and nothing for a user to clear first.

Deleting a single warranty is a real delete (no trash, no undo), consistent with the cross-cutting stance. **Archiving an item keeps its warranties** untouched — a boxed appliance still under coverage is exactly the case archive exists for.

## Audit

Every warranty create / update / delete is written to the audit log under its **own entity type, `ItemWarranty`**, with `ParentEntityId = ItemId`. Unlike notes, these rows **are projected into the item's activity feed**: "Alister added a warranty", "Alister updated a warranty (expiry)", "Alister removed a warranty (Bosch · until 2028-03-12)".

The two rules sit next to each other on purpose, and the split follows Documents' precedent exactly ([activity.md § Auditing notes](activity.md#auditing-notes)). `DocumentComment` is audited under its own type and *not* projected — an "edited a comment" row beside the comment itself is noise, and the comment is already visible. `DocumentVersion` *is* projected, because a new version is a change to the document. A warranty is the `DocumentVersion` case: it is structured data about the item, its rows live in a different section of the page than the feed, and when one is deleted the feed's line is the **only** remaining trace — which is precisely why the delete row carries a description of what was removed. A note, by contrast, is the `DocumentComment` case: it shows in the feed in its current state, or not at all.

## Future-compatibility

All additive on top of `inventory.item_warranties`, none requiring a re-migration:

- **Proof documents** — the receipt, the warranty card, the manual page — arrive with the generic **item → Documents link**, which is where every attachment story for Inventory lands. A *per-warranty* pointer is then a nullable column **on that link row**, not a document FK on the warranty: the link already carries the document's lifecycle and cascade semantics, and one seam serving receipts, manuals, and warranty cards beats three.
- **A `Kind` enum** (Manufacturer / Extended / Store) if a behaviour ever depends on the distinction, additive with a default; the null-provider convention keeps working alongside it.
- **A start date**, if coverage that has not begun yet ever matters. `expiresOn` stays the field the status reads.
- **Reminders and notifications** in the future Calendar/Maintenance module, which defines its own task identity rather than keying on a warranty row. Today a lapse surfaces only where the user is already looking.
- **A per-item or per-household lead time** replacing the 30-day constant — a column with the constant as its default.
- **Per-tier item status** (surfacing "partly expired") if the lapsed-tier case above ever proves annoying in practice; it needs the `Kind` enum first.
- **A Warranty column on the Items list** and a warranty chip on the quick panel, both currently excluded to keep those surfaces dense and fast.
- **Sync.** Warranty delete is a hard delete with no tombstone; the `ItemWarranty` / `Deleted` audit row is the only trace a future sync design would have to work from — the same caveat the overview records for notes.
