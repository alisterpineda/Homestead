import { formatDate } from '@angular/common';
import type { DurableResponse } from '@/api/generated/models/durable-response';

/**
 * The composed "Acquired" line shared by the durable detail page and quick panel: kind → date →
 * cost+symbol → "from {source}", each part present only when recorded, joined with ` · `.
 * Returns null when nothing about the acquisition is recorded.
 */
export function acquisitionSummary(d: DurableResponse, locale: string): string | null {
  const parts: string[] = [];
  if (d.acquisitionKind) parts.push(d.acquisitionKind);
  if (d.acquiredOn) parts.push(formatDate(d.acquiredOn, 'mediumDate', locale));
  if (d.acquisitionCost != null) {
    parts.push(`${d.acquisitionCost}${d.acquisitionCostCurrencySymbol ? ' ' + d.acquisitionCostCurrencySymbol : ''}`);
  }
  if (d.acquiredFrom) parts.push(`from ${d.acquiredFrom}`);
  return parts.length ? parts.join(' · ') : null;
}
