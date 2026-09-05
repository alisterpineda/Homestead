import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { PlaceConsumableResponse } from '@/api/generated/models/place-consumable-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { ItemTagDto } from '@/api/generated/models/item-tag-dto';
import type { WarrantyStatus } from '@/api/generated/models/warranty-status';
import { formatQty } from '../consumables/consumable-shared';
import { itemStatusLabel } from '../shared/item-status-meta';
import {
  durableLocationPath,
  locationPathText,
  stockEntryLocationPath,
  type LocationPathView,
} from '../shared/location-path';

export type InventoryKind = 'durable' | 'consumable';

/** The sortable columns of the unified inventory list — the single source of truth for the set. */
export const INVENTORY_SORT_IDS = ['name', 'status', 'location', 'onHand', 'updatedAt'] as const;
/** A sortable column id, derived from {@link INVENTORY_SORT_IDS} so the two never drift. */
export type InventorySortId = (typeof INVENTORY_SORT_IDS)[number];
/** A column + direction pair. */
export interface InventorySortState {
  readonly id: InventorySortId;
  readonly desc: boolean;
}

const SORT_IDS: ReadonlySet<string> = new Set(INVENTORY_SORT_IDS);

/**
 * The items-list initial sort from the `?sort`/`?dir` deep link (the Browse "View all →" recency
 * bridge). An absent or unknown `sort` id falls back to name-ascending, so ordinary navigation is
 * unaffected; `dir=desc` flips to descending, anything else (including absent) stays ascending.
 * Colocated with {@link InventorySortId} (mirroring `kindFilterFromParam`) so the validator can't
 * drift from the id set it validates against.
 */
export function sortStateFromParams(sort: string | null, dir: string | null): InventorySortState {
  if (sort && SORT_IDS.has(sort)) {
    return { id: sort as InventorySortId, desc: dir === 'desc' };
  }
  return { id: 'name', desc: false };
}

/**
 * Row view-model for the unified inventory list: a non-fungible durable or a fungible consumable
 * flattened to one shape so the table, filters, and sorting treat both kinds uniformly.
 */
export interface InventoryRow {
  readonly kind: InventoryKind;
  readonly id: string;
  readonly name: string;
  readonly status: ItemStatus;
  readonly updatedAt: string;
  /** Display/sort text for the Location column. Consumable: '—' (no stock) | label | 'N locations'. */
  readonly locationText: string;
  /**
   * Resolved root area per holding location; null = unplaced. A durable has exactly one entry, a
   * consumable one per stock entry — so a zero-stock consumable has none and matches no area filter.
   */
  readonly resolvedAreaIds: readonly (string | null)[];
  /** Total on hand for a consumable; null for a durable (renders as "—"). */
  readonly onHand: number | null;
  readonly unit: string | null;
  readonly isLow: boolean;
  /** Muted hint after the quantity (drilled consumable rows: "of N total"). */
  readonly onHandHint?: string;
  /** Classification tags on this item (both kinds), each carrying its resolved effective colour. */
  readonly tags: readonly ItemTagDto[];
  /**
   * The item's derived warranty state, computed server-side. Null on a consumable — warranties are
   * a durables-only concept, so a consumable row matches no warranty facet rather than reading
   * "None" (which is a real answer about a durable that has no warranty recorded).
   */
  readonly warrantyStatus: WarrantyStatus | null;
  readonly durable?: DurableResponse;
  readonly consumable?: ConsumableResponse;
}

/**
 * Flattens a durable's resolved location path to display/sort text ("Garage › Cupboard › Bin").
 * The branches themselves live in {@link durableLocationPath}, which the linked surfaces render as
 * crumbs — this is the same rule flattened, so text and links can never disagree.
 */
export function formatPath(durable: DurableResponse): string {
  return locationPathText(durableLocationPath(durable));
}

export function durableToRow(durable: DurableResponse): InventoryRow {
  return {
    kind: 'durable',
    id: durable.id,
    name: durable.name,
    status: durable.status,
    updatedAt: durable.updatedAt,
    locationText: formatPath(durable),
    resolvedAreaIds: [durable.resolvedAreaId ?? null],
    onHand: null,
    unit: null,
    isLow: false,
    tags: durable.tags ?? [],
    warrantyStatus: durable.warrantyStatus ?? null,
    durable,
  };
}

/**
 * Resolved (leaf) area of one stock entry: the LAST Area-kind segment of the path. The path now
 * carries the full area-ancestor chain (House › Basement › Bedroom) before any container segments,
 * so the leaf area — the one the entry literally resolves to — is the last Area segment, not the
 * first. Null when the entry resolves to no area (an Unplaced container chain).
 */
function entryResolvedAreaId(entry: StockEntryResponse): string | null {
  let resolved: string | null = null;
  for (const seg of entry.path)
    if (seg.kind === 'Area') resolved = seg.id;
  return resolved;
}

export function consumableLocationText(locations: readonly StockEntryResponse[]): string {
  if (locations.length === 0) return '—';
  if (locations.length === 1) return locations[0].locationLabel ?? 'Unplaced';
  return `${locations.length} locations`;
}

export function consumableToRow(consumable: ConsumableResponse): InventoryRow {
  const locations = consumable.stockLocations ?? [];
  return {
    kind: 'consumable',
    id: consumable.id,
    name: consumable.name,
    status: consumable.status,
    updatedAt: consumable.updatedAt,
    locationText: consumableLocationText(locations),
    resolvedAreaIds: locations.map(entryResolvedAreaId),
    onHand: consumable.totalOnHand,
    unit: consumable.unit ?? null,
    isLow: consumable.isLow,
    tags: consumable.tags ?? [],
    warrantyStatus: null,
    consumable,
  };
}

/**
 * Place-scoped view of a consumable (Browse place contents): the LOCAL quantity at the drilled
 * place, carried by `PlaceConsumableResponse.localQuantity`, with a muted "of N total" hint when
 * more sits elsewhere. The local quantity is supplied directly by the place-contents payload, and
 * the (Browse-hidden) Location column keeps the base consumable's text.
 */
export function placeConsumableToRow(pc: PlaceConsumableResponse): InventoryRow {
  const total = pc.consumable.totalOnHand;
  return {
    ...consumableToRow(pc.consumable),
    onHand: pc.localQuantity,
    onHandHint: pc.localQuantity !== total ? `of ${formatQty(total)} total` : undefined,
  };
}

/**
 * What a row's Location reads as, before any one surface decides how to draw it: a real path worth
 * rendering as crumbs, an italic placeholder standing in for "nowhere", or plain summary text.
 */
export type LocationCellView =
  /** Render as crumbs (collapsed) — a durable's resolved path, or a consumable's single stock entry. */
  | { readonly kind: 'path'; readonly view: LocationPathView }
  /** Nothing to link to: no stock at all ('—'), or an item that resolves to the Unplaced bucket. */
  | { readonly kind: 'placeholder'; readonly text: 'Unplaced' | '—' }
  /** Flat summary text, e.g. "3 locations" or an unresolved stock label. */
  | { readonly kind: 'text'; readonly text: string };

/**
 * The Location branch rule for one row, decided once for every surface that shows it (the table
 * cell, the card). Kept beside the row model rather than inside a renderer so the table and the
 * cards can never disagree about *what* a row's location is while differing on how it's drawn.
 */
export function locationCellView(row: InventoryRow): LocationCellView {
  const durable = row.durable;
  if (durable) {
    const view = durableLocationPath(durable);
    // A lone muted crumb with no prefix is the synthetic Unplaced root standing alone — there is no
    // real path to render, so it reads as the placeholder rather than a one-crumb trail.
    if (view.crumbs.length === 1 && view.crumbs[0].muted && !view.prefix) {
      return { kind: 'placeholder', text: 'Unplaced' };
    }
    return { kind: 'path', view };
  }
  // A consumable held in exactly one place reads like a durable's path; spread stock is a count,
  // and stock whose entry resolved to nothing keeps the flat label below.
  const single = row.consumable?.stockLocations;
  if (single?.length === 1 && single[0].path.length > 0) {
    return { kind: 'path', view: stockEntryLocationPath(single[0]) };
  }
  const text = row.locationText;
  if (text === '—' || text === 'Unplaced') {
    return { kind: 'placeholder', text };
  }
  return { kind: 'text', text };
}

function compareNullable<T>(av: T | null, bv: T | null, dir: 1 | -1, lt: (a: T, b: T) => number): number {
  if (av === bv) return 0;
  if (av === null) return 1;
  if (bv === null) return -1;
  return lt(av, bv) * dir;
}

/**
 * Pure comparator-sort over inventory rows by the chosen column/direction. Returns a new array.
 * Durables (no quantity, null `onHand`/`updatedAt`) always sort last regardless of direction — see
 * {@link compareNullable}. Shared by the items-view component and tests so a single ordering rule
 * backs every surface.
 */
export function sortRows(rows: readonly InventoryRow[], sortState: InventorySortState): InventoryRow[] {
  const result = [...rows];
  const { id, desc } = sortState;
  const dir: 1 | -1 = desc ? -1 : 1;
  result.sort((a, b) => {
    switch (id) {
      case 'name':
        return a.name.localeCompare(b.name) * dir;
      case 'status':
        return itemStatusLabel(a.status).localeCompare(itemStatusLabel(b.status)) * dir;
      case 'location':
        return a.locationText.localeCompare(b.locationText) * dir;
      case 'onHand':
        return compareNullable(a.onHand, b.onHand, dir, (x, y) => x - y);
      case 'updatedAt':
        return compareNullable(a.updatedAt ?? null, b.updatedAt ?? null, dir, (x, y) => (x < y ? -1 : 1));
      default:
        return 0;
    }
  });
  return result;
}
