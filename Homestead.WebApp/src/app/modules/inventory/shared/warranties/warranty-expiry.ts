import { formatDate } from '@angular/common';

/** The one-tap warranty lengths offered by the composer's expiry chip, in years. */
export const EXPIRY_PRESETS: readonly number[] = [1, 2, 3, 5];

/** Days in a Gregorian month (1-based month), so a preset can clamp rather than roll over. */
function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/**
 * Calendar-adds `years` to a `yyyy-MM-dd` string, staying entirely in string/number space so no
 * timezone can shift the result by a day.
 *
 * The only ambiguous input is 29 February: adding a whole number of years to it lands on a date that
 * mostly doesn't exist, so it **clamps to 28 February** rather than rolling into 1 March. That
 * matches how a warranty is actually read ("three years from the leap day" ends in February), and it
 * is what `new Date()` arithmetic would silently get wrong.
 */
export function addYearsIso(baseIso: string, years: number): string {
  const [y, m, d] = baseIso.slice(0, 10).split('-').map(Number);
  if (!y || !m || !d) return baseIso;
  const year = y + years;
  const day = Math.min(d, daysInMonth(year, m));
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${year}-${pad(m)}-${pad(day)}`;
}

/**
 * A preset's menu label — the length plus the date it actually resolves to ("2 years · Mar 12, 2028"),
 * so the user picks a *date* and not a duration whose base (acquired-on, else today) they'd have to
 * remember.
 */
export function presetLabel(years: number, resultIso: string, locale: string): string {
  const noun = years === 1 ? 'year' : 'years';
  return `${years} ${noun} · ${formatDate(resultIso, 'mediumDate', locale)}`;
}
