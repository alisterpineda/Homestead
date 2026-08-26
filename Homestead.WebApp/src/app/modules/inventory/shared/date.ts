import { parseDate } from '@/shared/utils';

/** `yyyy-MM-dd` for tomorrow (UTC) — the max selectable acquired-on date (today + 1 day slack). */
export function tomorrowIso(): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

/** `tomorrowIso()` as a local Date — the cap for hlm-date-picker `[maxDate]`. */
export function tomorrowDate(): Date {
  return parseDate(tomorrowIso())!;
}
