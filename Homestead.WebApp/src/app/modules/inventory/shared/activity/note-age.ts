import { formatDate } from '@angular/common';
import { formatRelativeAge, SKEW_TOLERANCE_MS } from '@/shared/utils/relative-age';

/**
 * A note's headline time is `OccurredAt` — when the thing *happened*, not when it was typed —
 * so a back-filled 2019 service entry must never read "2 minutes ago". Recent instants stay
 * relative (the Documents idiom, and the only form that reads naturally beside audit rows);
 * anything older than a week renders as an absolute date instead.
 */
const RELATIVE_WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

export function formatNoteAge(timestamp: string | null | undefined, locale: string): string {
  if (!timestamp) return '';
  const then = new Date(timestamp).getTime();
  if (Number.isNaN(then)) return '';
  const diffMs = Date.now() - then;
  // Shares `formatRelativeAge`'s skew window: a timestamp slightly ahead of the client clock is skew
  // on a just-written note, not a real future date, so it still reads relative ("just now").
  if (diffMs < RELATIVE_WINDOW_MS && diffMs >= -SKEW_TOLERANCE_MS) return formatRelativeAge(timestamp);
  return formatDate(timestamp, 'mediumDate', locale);
}
