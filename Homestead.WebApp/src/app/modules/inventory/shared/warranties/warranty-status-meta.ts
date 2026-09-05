import { formatDate } from '@angular/common';
import { BADGE_BASE, LOW_STOCK_BADGE_CLASS } from '../item-status-meta';
import type { WarrantyStatus } from '@/api/generated/models/warranty-status';

export interface WarrantyStatusMeta {
  readonly label: string;
  /** Full Tailwind class string for the status pill (self-contained — not an hlmBadge variant). */
  readonly badgeClass: string;
  /** A resting state: rendered as no pill at all (see {@link warrantyBadge}). */
  readonly isDefault?: boolean;
}

/**
 * The four derived warranty states, as the UI draws them. Only the two that want the eye get a pill:
 * **Expiring** borrows the amber low-stock tone (the module's one "act soon" colour, so the two
 * cannot read as different urgencies), **Expired** is the quiet muted tone shared with Archived.
 * *Active* and *None* are resting states — a covered item and an uncovered one are both unremarkable,
 * and a pill on every row would drown the two that matter.
 *
 * Row-level status is never `None` (the server's row rule maps a null expiry to Active), so that
 * entry exists for the per-item rollup on `DurableResponse.warrantyStatus` and the list facet.
 */
export const WARRANTY_STATUS_META: Record<WarrantyStatus, WarrantyStatusMeta> = {
  None: {
    label: 'No warranty',
    badgeClass: `${BADGE_BASE} bg-muted text-muted-foreground`,
    isDefault: true,
  },
  Active: {
    label: 'Active',
    badgeClass: `${BADGE_BASE} bg-secondary text-secondary-foreground`,
    isDefault: true,
  },
  Expiring: {
    label: 'Expiring soon',
    // Literally the low-stock pill: the module keeps exactly one "act soon" amber, so an expiring
    // warranty and a low consumable can never read as two different urgencies. (Its `gap-1` is a
    // no-op here — this pill has a single text child.)
    badgeClass: LOW_STOCK_BADGE_CLASS,
  },
  Expired: {
    label: 'Expired',
    badgeClass: `${BADGE_BASE} bg-muted text-muted-foreground`,
  },
};

/** The pill for a status, or null for a resting state that renders nothing. */
export function warrantyBadge(status: WarrantyStatus): WarrantyStatusMeta | null {
  const meta = WARRANTY_STATUS_META[status];
  return !meta || meta.isDefault ? null : meta;
}

/** The single-choice Warranty lens on the items list, and the `?warranty=` deep-link vocabulary. */
export type WarrantyFacet = 'any' | 'active' | 'expiring' | 'expired' | 'none';

export interface WarrantyFacetOption {
  readonly value: WarrantyFacet;
  readonly label: string;
  /** The row status this facet keeps; null = no filtering ("Any"). */
  readonly status: WarrantyStatus | null;
}

/**
 * The facet options in filter order. Colocated with the validator below (the `kindFilterFromParam`
 * precedent) so the `?warranty=` reader can never drift from the set it validates against.
 */
export const WARRANTY_FACETS: readonly WarrantyFacetOption[] = [
  { value: 'any', label: 'Any', status: null },
  { value: 'active', label: 'Active', status: 'Active' },
  { value: 'expiring', label: 'Expiring soon', status: 'Expiring' },
  { value: 'expired', label: 'Expired', status: 'Expired' },
  { value: 'none', label: 'None', status: 'None' },
];

export const isWarrantyFacet = (v: unknown): v is WarrantyFacet =>
  WARRANTY_FACETS.some(f => f.value === v);

/** An absent or unknown `?warranty=` value falls back to the unfiltered 'any'. */
export const warrantyFacetFromParam = (v: string | null): WarrantyFacet =>
  isWarrantyFacet(v) ? v : 'any';

/**
 * How a warranty row names who is standing behind it.
 *
 * A null `provider` means **the manufacturer** — the household's default assumption, never copied
 * into the row (the brand can be renamed or corrected later, and a stored copy would go stale). It
 * is resolved here at display time: with the item's brand known it reads "Bosch · manufacturer", and
 * without one it falls back to the bare word. A stored provider (a retailer, an extended-warranty
 * underwriter) is shown verbatim.
 */
export function warrantyProviderLabel(
  provider: string | null | undefined,
  brandName: string | null | undefined,
): string {
  const named = provider?.trim();
  if (named) return named;
  const brand = brandName?.trim();
  return brand ? `${brand} · manufacturer` : 'Manufacturer';
}

/**
 * The muted second line of a warranty row: "until Mar 12, 2028" / "expired Mar 12, 2027" / "no expiry".
 * The tense comes from the server-derived row status rather than a client-side date comparison, so
 * the row can't disagree with its own pill across a timezone boundary.
 */
export function warrantyExpiryText(
  expiresOn: string | null | undefined,
  status: WarrantyStatus,
  locale: string,
): string {
  if (!expiresOn) return 'no expiry';
  const when = formatDate(expiresOn, 'mediumDate', locale);
  return status === 'Expired' ? `expired ${when}` : `until ${when}`;
}
