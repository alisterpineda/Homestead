import type { StockMovementResponse } from '@/api/generated/models/stock-movement-response';

/** Formats a quantity for display, trimming insignificant trailing zeros (e.g. 2.50 → "2.5"). */
export function formatQty(value: number): string {
  if (!Number.isFinite(value)) return '0';
  // Up to 3 decimals, then strip trailing zeros and any dangling decimal point.
  return value.toFixed(3).replace(/\.?0+$/, '');
}

/** Generic fallback word rendered when a consumable has no unit of its own. */
export const DEFAULT_UNIT = 'units';

/**
 * Resolves a stored unit for display: the user's unit when set, otherwise the generic
 * {@link DEFAULT_UNIT} fallback. Storage stays NULL when no unit was chosen — the fallback is a
 * display-time concern only.
 */
export function resolveUnit(unit: string | null | undefined): string {
  return unit?.trim() || DEFAULT_UNIT;
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
