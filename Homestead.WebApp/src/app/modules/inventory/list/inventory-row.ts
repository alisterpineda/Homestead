import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { PlaceConsumableResponse } from '@/api/generated/models/place-consumable-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { formatQty } from '../consumables/consumable-shared';
import { itemStatusLabel } from '../shared/item-status-meta';

export type InventoryKind = 'durable' | 'consumable';

/** The sortable columns of the unified inventory list, and a column+direction pair. */
export type InventorySortId = 'name' | 'status' | 'location' | 'onHand' | 'updatedAt';
export interface InventorySortState {
  readonly id: InventorySortId;
  readonly desc: boolean;
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
  readonly durable?: DurableResponse;
  readonly consumable?: ConsumableResponse;
}

/**
 * Flattens a durable's resolved location path to display/sort text ("Garage › Cupboard › Bin"). A
 * directly-unplaced durable, or one whose chain resolves to Unplaced, is prefixed with "Unplaced".
 */
export function formatPath(durable: DurableResponse): string {
  const names = (durable.path ?? []).map(s => s.name);
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

function consumableLocationText(locations: readonly StockEntryResponse[]): string {
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
    consumable,
  };
}

/**
 * Drill-down view of a consumable scoped to one container: the LOCAL quantity at that container with
 * a muted "of N total" hint when more sits elsewhere. Null when the consumable has no stock there.
 */
export function consumableToDrillRow(consumable: ConsumableResponse, containerItemId: string): InventoryRow | null {
  const entry = (consumable.stockLocations ?? []).find(e => e.containerItemId === containerItemId);
  if (!entry) return null;
  return {
    ...consumableToRow(consumable),
    locationText: entry.locationLabel ?? 'Unplaced',
    onHand: entry.quantity,
    onHandHint: entry.quantity !== consumable.totalOnHand
      ? `of ${formatQty(consumable.totalOnHand)} total`
      : undefined,
  };
}

/**
 * Place-scoped view of a consumable (Browse drill-down): the LOCAL quantity at the drilled place,
 * carried by `PlaceConsumableResponse.localQuantity`, with a muted "of N total" hint when more sits
 * elsewhere. Unlike {@link consumableToDrillRow}, the local quantity is supplied directly by the
 * place-contents payload rather than reconstructed from a stock entry, and the (Browse-hidden)
 * Location column keeps the base consumable's text.
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
