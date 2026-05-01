import type { NetWorthRangePref } from '@/shared/services/user-preferences.service';

export interface ResolvedWindow {
  readonly from: Date | null;
  readonly to: Date | null;
}

/**
 * Mirrors `GetNetWorthHistoryHandler.ResolveRange` so the selector can render the
 * label and chevron-step math without waiting for the server. `from`/`to` are
 * `Date`s pinned to local midnight — the formatter and ECharts both render in
 * local TZ, so anchoring "today" to the user's wall clock keeps the label and
 * axis bounds aligned with what they expect. For `All`, both are null — the
 * caller falls back to the server's response window.
 */
export function resolveWindow(pref: NetWorthRangePref, today: Date): ResolvedWindow | null {
  const todayLocal = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  switch (pref.kind) {
    case 'ThisMonth': {
      const from = new Date(todayLocal.getFullYear(), todayLocal.getMonth(), 1);
      return { from, to: todayLocal };
    }
    case 'ThreeMonths':
      return priorCalendarMonthsWindow(todayLocal, 3);
    case 'SixMonths':
      return priorCalendarMonthsWindow(todayLocal, 6);
    case 'TwelveMonths':
      return priorCalendarMonthsWindow(todayLocal, 12);
    case 'YearToDate': {
      const from = new Date(todayLocal.getFullYear(), 0, 1);
      return { from, to: todayLocal };
    }
    case 'LastYear': {
      const year = todayLocal.getFullYear() - 1;
      return {
        from: new Date(year, 0, 1),
        to: new Date(year, 11, 31),
      };
    }
    case 'All':
      return { from: null, to: null };
    case 'Custom': {
      if (!pref.from || !pref.to) return null;
      return {
        from: new Date(`${pref.from}T00:00:00`),
        to: new Date(`${pref.to}T00:00:00`),
      };
    }
  }
}

// Calendar-aligned window of the last N calendar months including the current
// month. Mirrors the backend's PriorCalendarMonthsWindow.
function priorCalendarMonthsWindow(today: Date, months: number): ResolvedWindow {
  const year = today.getFullYear();
  const month = today.getMonth();
  const from = new Date(year, month - (months - 1), 1);
  // (year, month + 1, 0) → last day of current month
  const to = new Date(year, month + 1, 0);
  return { from, to };
}
