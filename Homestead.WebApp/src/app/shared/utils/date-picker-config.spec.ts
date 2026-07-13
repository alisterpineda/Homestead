import { describe, expect, it } from 'vitest';

import { formatPickerDate, parsePickerDate } from './date-picker-config';

describe('formatPickerDate', () => {
  it('formats a local date in friendly form', () => {
    expect(formatPickerDate(new Date(2026, 6, 12))).toBe('Jul 12, 2026');
  });
});

describe('parsePickerDate', () => {
  it('parses ISO date strings as local dates (no UTC day shift)', () => {
    const d = parsePickerDate('2026-07-12');
    expect(d).toBeDefined();
    expect(d!.getFullYear()).toBe(2026);
    expect(d!.getMonth()).toBe(6);
    expect(d!.getDate()).toBe(12);
  });

  it('parses ISO date-time strings via the local-safe ISO branch', () => {
    const d = parsePickerDate('2026-07-12T00:00:00Z');
    expect(d).toBeDefined();
    expect(d!.getFullYear()).toBe(2026);
    expect(d!.getMonth()).toBe(6);
    expect(d!.getDate()).toBe(12);
  });

  it('round-trips the friendly display format', () => {
    const d = parsePickerDate('Jul 12, 2026');
    expect(d).toBeDefined();
    expect(d!.getFullYear()).toBe(2026);
    expect(d!.getMonth()).toBe(6);
    expect(d!.getDate()).toBe(12);
  });

  it('returns null for unparseable or empty input', () => {
    expect(parsePickerDate('garbage')).toBeNull();
    expect(parsePickerDate('')).toBeNull();
    expect(parsePickerDate('   ')).toBeNull();
  });

  // hlm-date-picker-input re-parses the displayed text on every blur, so the
  // format → parse round trip must never lose the date (regression: a browser-
  // locale display format broke this for non-English locales).
  it('round-trips formatPickerDate output for arbitrary dates', () => {
    for (const d of [new Date(2026, 0, 1), new Date(2026, 8, 30), new Date(1999, 11, 31)]) {
      const parsed = parsePickerDate(formatPickerDate(d));
      expect(parsed).toBeDefined();
      expect(parsed!.getFullYear()).toBe(d.getFullYear());
      expect(parsed!.getMonth()).toBe(d.getMonth());
      expect(parsed!.getDate()).toBe(d.getDate());
    }
  });
});
