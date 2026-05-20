import type { NetWorthRangePref } from '@/shared/services/user-preferences.service';

export interface ResolvedWindow {
  readonly from: Date | null;
  readonly to: Date | null;
}

/**
 * Mirrors `GetNetWorthHistoryHandler.ResolveRange` so the selector can render
 * the label and chevron-step math without waiting for the server. `from` is
 * local midnight of the first day; `to` is local end-of-day of the last day so
 * `to.toISOString()` aligns with the backend's `AsOf` (also local end-of-today)
 * — otherwise ECharts' xAxisMax falls at local midnight and clips the last
 * point, making the line appear to end at "yesterday." For `All`, both are
 * null — the caller falls back to the server's response window.
 */
export function resolveWindow(pref: NetWorthRangePref, today: Date): ResolvedWindow | null {
  const endOfToday = new Date(
    today.getFullYear(), today.getMonth(), today.getDate(), 23, 59, 59, 999);
  switch (pref.kind) {
    case 'ThisMonth': {
      const from = new Date(endOfToday.getFullYear(), endOfToday.getMonth(), 1);
      return { from, to: endOfToday };
    }
    case 'ThreeMonths':
      return priorCalendarMonthsWindow(endOfToday, 3);
    case 'SixMonths':
      return priorCalendarMonthsWindow(endOfToday, 6);
    case 'TwelveMonths':
      return priorCalendarMonthsWindow(endOfToday, 12);
    case 'YearToDate': {
      const from = new Date(endOfToday.getFullYear(), 0, 1);
      return { from, to: endOfToday };
    }
    case 'LastYear': {
      const year = endOfToday.getFullYear() - 1;
      return {
        from: new Date(year, 0, 1),
        to: new Date(year, 11, 31, 23, 59, 59, 999),
      };
    }
    case 'All':
      return { from: null, to: null };
    case 'Custom': {
      if (!pref.from || !pref.to) return null;
      return {
        from: new Date(`${pref.from}T00:00:00`),
        to: new Date(`${pref.to}T23:59:59.999`),
      };
    }
  }
}

// Calendar-aligned window of the last N calendar months including the current
// month. Mirrors the backend's PriorCalendarMonthsWindow. The end-of-month
// upper bound is clamped to `today` so the chart axis doesn't extend past
// today into empty future days.
function priorCalendarMonthsWindow(today: Date, months: number): ResolvedWindow {
  const year = today.getFullYear();
  const month = today.getMonth();
  const from = new Date(year, month - (months - 1), 1);
  const endOfMonth = new Date(year, month + 1, 0, 23, 59, 59, 999);
  const to = endOfMonth > today ? today : endOfMonth;
  return { from, to };
}
