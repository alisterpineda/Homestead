import type { ItemStatus } from '@/api/generated/models/item-status';
import type { StockMovementResponse } from '@/api/generated/models/stock-movement-response';

/** Supplies use the reduced fungible lifecycle — only these two statuses are valid. */
export const SUPPLY_STATUSES: readonly ItemStatus[] = ['Active', 'Archived'];

/** Formats a quantity for display, trimming insignificant trailing zeros (e.g. 2.50 → "2.5"). */
export function formatQty(value: number): string {
  if (!Number.isFinite(value)) return '0';
  // Up to 3 decimals, then strip trailing zeros and any dangling decimal point.
  return value.toFixed(3).replace(/\.?0+$/, '');
}

/** "12 rolls" — a quantity with its unit. */
export function formatQtyWithUnit(value: number, unit: string): string {
  return `${formatQty(value)} ${unit}`.trim();
}

/**
 * Whole-number proxy for "count-like": track-one-individually is only offered when a location holds
 * a whole number of discrete units (≥ 1). The backend enforces the same rule; the UI hides the
 * action for fractional/continuous units rather than disabling it.
 */
export function isWholeCount(q: number): boolean {
  return Number.isInteger(q) && q >= 1;
}

export type MovementDirection = 'inflow' | 'outflow' | 'transfer' | 'unknown';

/** Direction is structural — read from which of the From/To ids are populated. */
export function movementDirection(m: StockMovementResponse): MovementDirection {
  const hasFrom = m.fromAreaId != null || m.fromContainerItemId != null;
  const hasTo = m.toAreaId != null || m.toContainerItemId != null;
  if (hasFrom && hasTo) return 'transfer';
  if (hasTo) return 'inflow';
  if (hasFrom) return 'outflow';
  return 'unknown';
}
