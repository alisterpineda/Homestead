import { computed, type Signal, type WritableSignal } from '@angular/core';
import { parseDate, toIsoDateString } from '@/shared/utils';

/** Friendly display, e.g. 'Jul 12, 2026'. Locale is pinned to en-US (matching the
 *  app's English-only UI and default LOCALE_ID) because hlm-date-picker-input
 *  re-parses the displayed text on every blur: a locale-dependent format that
 *  `parsePickerDate`'s `new Date()` fallback can't read (e.g. fr-FR '12 juil. 2026')
 *  would silently clear the field on focus+blur. */
export function formatPickerDate(date: Date): string {
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

/** Lenient, local-safe parse: ISO-leading strings parse as LOCAL y/m/d (no UTC day
 *  shift); anything else falls back to `new Date(value)`; invalid → null.
 *  (Spartan's `HlmDatePickerConfig.parseDate` contract returns `T | null`.) */
export function parsePickerDate(value: string): Date | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  if (/^\d{4}-\d{2}-\d{2}/.test(trimmed)) return parseDate(trimmed) ?? null;
  const d = new Date(trimmed);
  return isNaN(d.getTime()) ? null : d;
}

/** Bridge a `'yyyy-MM-dd'` string signal (empty string = no date) to
 *  hlm-date-picker's Date bindings: `[date]="x.date()" (dateChange)="x.set($event)"`. */
export function isoDateBinding(sig: WritableSignal<string>): {
  date: Signal<Date | undefined>;
  set: (date: Date | null | undefined) => void;
} {
  return {
    date: computed(() => parseDate(sig()) ?? undefined),
    set: (date) => sig.set(date ? toIsoDateString(date)! : ''),
  };
}
