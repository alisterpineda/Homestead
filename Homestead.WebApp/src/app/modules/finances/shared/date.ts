export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

export function tomorrowIso(): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

export function toDateOnly(isoString: string | null | undefined): string {
  return (isoString ?? '').slice(0, 10);
}

// `yyyy-MM-dd` of a Date in the user's local TZ. Distinct from the
// UTC-based `toISOString().slice(0, 10)` for callers who want the user's
// wall-clock day.
export function localIsoDate(d: Date): string {
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Local end-of-day instant in ISO 8601 with the user's TZ offset, e.g.
// `2026-05-01T23:59:59.999-07:00`. Round-trips through C# `DateTimeOffset.Parse`.
export function localEndOfTodayIso(): string {
  const now = new Date();
  const eod = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 23, 59, 59, 999);
  const pad = (n: number) => String(n).padStart(2, '0');
  const hours = pad(eod.getHours());
  const minutes = pad(eod.getMinutes());
  const seconds = pad(eod.getSeconds());
  const ms = String(eod.getMilliseconds()).padStart(3, '0');
  // getTimezoneOffset returns minutes WEST of UTC — negate so e.g. JST gives '+09:00'.
  const offsetMin = -eod.getTimezoneOffset();
  const sign = offsetMin >= 0 ? '+' : '-';
  const absMin = Math.abs(offsetMin);
  const offset = `${sign}${pad(Math.floor(absMin / 60))}:${pad(absMin % 60)}`;
  return `${localIsoDate(eod)}T${hours}:${minutes}:${seconds}.${ms}${offset}`;
}
