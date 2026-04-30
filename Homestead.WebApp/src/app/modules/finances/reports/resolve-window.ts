import type { NetWorthRangePref } from '@/shared/services/user-preferences.service';

export interface ResolvedWindow {
  readonly from: Date | null;
  readonly to: Date | null;
}

/**
 * Mirrors `GetNetWorthHistoryHandler.ResolveRange` so the selector can render the
 * label and chevron-step math without waiting for the server. `from`/`to` are
 * Date objects in UTC. For `All`, both are null — the caller falls back to the
 * server's response window.
 */
export function resolveWindow(pref: NetWorthRangePref, today: Date): ResolvedWindow | null {
  const todayUtc = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate()));
  switch (pref.kind) {
    case 'ThisMonth': {
      const from = new Date(Date.UTC(todayUtc.getUTCFullYear(), todayUtc.getUTCMonth(), 1));
      return { from, to: todayUtc };
    }
    case 'ThreeMonths':
      return priorCalendarMonthsWindow(todayUtc, 3);
    case 'SixMonths':
      return priorCalendarMonthsWindow(todayUtc, 6);
    case 'TwelveMonths':
      return priorCalendarMonthsWindow(todayUtc, 12);
    case 'YearToDate': {
      const from = new Date(Date.UTC(todayUtc.getUTCFullYear(), 0, 1));
      return { from, to: todayUtc };
    }
    case 'LastYear': {
      const year = todayUtc.getUTCFullYear() - 1;
      return {
        from: new Date(Date.UTC(year, 0, 1)),
        to: new Date(Date.UTC(year, 11, 31)),
      };
    }
    case 'All':
      return { from: null, to: null };
    case 'Custom': {
      if (!pref.from || !pref.to) return null;
      return {
        from: new Date(`${pref.from}T00:00:00Z`),
        to: new Date(`${pref.to}T00:00:00Z`),
      };
    }
  }
}

// Calendar-aligned window of the last N calendar months including the current
// month. Mirrors the backend's PriorCalendarMonthsWindow.
function priorCalendarMonthsWindow(today: Date, months: number): ResolvedWindow {
  const year = today.getUTCFullYear();
  const month = today.getUTCMonth();
  const from = new Date(Date.UTC(year, month - (months - 1), 1));
  // (year, month + 1, 0) → last day of current month
  const to = new Date(Date.UTC(year, month + 1, 0));
  return { from, to };
}
