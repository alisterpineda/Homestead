import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { formatQty } from '../consumables/consumable-shared';

export type InventoryKind = 'durable' | 'consumable';

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
    unit: consumable.unit,
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
