import type { DurableResponse } from '@/api/generated/models/durable-response';
import { acquisitionSummary } from './acquisition-summary';

/**
 * Whether a durable has anything to show in its Details box (value, serial, model, brand, acquired,
 * lent-to, description). Shared by the durable detail page and quick panel so the two surfaces can
 * never disagree about when the box renders; when a row is added to either `<dl>`, add it here too.
 */
export function hasDurableDetails(d: DurableResponse | null | undefined, locale: string): boolean {
  return !!d && (d.value != null || !!d.serial || !!d.model || !!d.brandId
    || acquisitionSummary(d, locale) !== null
    || (d.status === 'Lent' && !!d.lentTo) || !!d.notes);
}
