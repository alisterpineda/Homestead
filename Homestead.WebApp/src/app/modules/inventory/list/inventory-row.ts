import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { formatQty } from '../supplies/supply-shared';

export type InventoryKind = 'item' | 'supply';

/**
 * Row view-model for the unified inventory list: a non-fungible item or a fungible supply
 * flattened to one shape so the table, filters, and sorting treat both kinds uniformly.
 */
export interface InventoryRow {
  readonly kind: InventoryKind;
  readonly id: string;
  readonly name: string;
  readonly status: ItemStatus;
  readonly updatedAt: string;
  /** Display/sort text for the Location column. Supply: '—' (no stock) | label | 'N locations'. */
  readonly locationText: string;
  /**
   * Resolved root area per holding location; null = unplaced. An item has exactly one entry, a
   * supply one per stock entry — so a zero-stock supply has none and matches no area filter.
   */
  readonly resolvedAreaIds: readonly (string | null)[];
  /** Total on hand for a supply; null for an item (renders as "—"). */
  readonly onHand: number | null;
  readonly unit: string | null;
  readonly isLow: boolean;
  /** Muted hint after the quantity (drilled supply rows: "of N total"). */
  readonly onHandHint?: string;
  readonly item?: ItemResponse;
  readonly supply?: SupplyResponse;
}

/**
 * Flattens an item's resolved location path to display/sort text ("Garage › Cupboard › Bin"). A
 * directly-unplaced item, or one whose chain resolves to Unplaced, is prefixed with "Unplaced".
 */
export function formatPath(item: ItemResponse): string {
  const names = (item.path ?? []).map(s => s.name);
  if (names.length === 0) return 'Unplaced';
  return (item.resolvedAreaId == null ? ['Unplaced', ...names] : names).join(' › ');
}

export function itemToRow(item: ItemResponse): InventoryRow {
  return {
    kind: 'item',
    id: item.id,
    name: item.name,
    status: item.status,
    updatedAt: item.updatedAt,
    locationText: formatPath(item),
    resolvedAreaIds: [item.resolvedAreaId ?? null],
    onHand: null,
    unit: null,
    isLow: false,
    item,
  };
}

/** Root area of one stock entry: the path root when it is an area, else unplaced. */
function entryResolvedAreaId(entry: StockEntryResponse): string | null {
  const root = entry.path[0];
  return root?.kind === 'Area' ? root.id : null;
}

function supplyLocationText(locations: readonly StockEntryResponse[]): string {
  if (locations.length === 0) return '—';
  if (locations.length === 1) return locations[0].locationLabel ?? 'Unplaced';
  return `${locations.length} locations`;
}

export function supplyToRow(supply: SupplyResponse): InventoryRow {
  const locations = supply.stockLocations ?? [];
  return {
    kind: 'supply',
    id: supply.id,
    name: supply.name,
    status: supply.status,
    updatedAt: supply.updatedAt,
    locationText: supplyLocationText(locations),
    resolvedAreaIds: locations.map(entryResolvedAreaId),
    onHand: supply.totalOnHand,
    unit: supply.unit,
    isLow: supply.isLow,
    supply,
  };
}

/**
 * Drill-down view of a supply scoped to one container: the LOCAL quantity at that container with a
 * muted "of N total" hint when more sits elsewhere. Null when the supply has no stock there.
 */
export function supplyToDrillRow(supply: SupplyResponse, containerItemId: string): InventoryRow | null {
  const entry = (supply.stockLocations ?? []).find(e => e.containerItemId === containerItemId);
  if (!entry) return null;
  return {
    ...supplyToRow(supply),
    locationText: entry.locationLabel ?? 'Unplaced',
    onHand: entry.quantity,
    onHandHint: entry.quantity !== supply.totalOnHand
      ? `of ${formatQty(supply.totalOnHand)} total`
      : undefined,
  };
}
