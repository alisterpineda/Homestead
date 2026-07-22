import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { PlaceConsumableResponse } from '@/api/generated/models/place-consumable-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import type { ItemTagDto } from '@/api/generated/models/item-tag-dto';
import { formatQty } from '../consumables/consumable-shared';
import { itemStatusLabel } from '../shared/item-status-meta';

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
  readonly durable?: DurableResponse;
  readonly consumable?: ConsumableResponse;
}

/**
 * Flattens a durable's resolved location path to display/sort text ("Garage › Cupboard › Bin"). A
 * directly-unplaced durable, or one whose chain resolves to Unplaced, is prefixed with "Unplaced".
 * A component (child of a non-container) reads as composition, not storage: "Part of {parent}".
 */
export function formatPath(durable: DurableResponse): string {
  const names = (durable.path ?? []).map(s => s.name);
  // A component's immediate parent (the non-container) is the last path segment; fall through to the
  // storage rendering only if it's somehow missing, rather than emitting a placeholder.
  const parent = names.at(-1);
  if (durable.parentIsContainer === false && parent) return `Part of ${parent}`;
  if (names.length === 0) return 'Unplaced';
  return (durable.resolvedAreaId == null ? ['Unplaced', ...names] : names).join(' › ');
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
