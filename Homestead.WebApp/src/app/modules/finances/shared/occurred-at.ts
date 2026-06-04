// Helpers for the `occurredAt` instant carried by the per-shape transaction editors
// (trade, dividend). The UI splits the instant into a native `<input type="date">`
// plus an optional `<input type="time">`. The time field is *collapsed* by default;
// when collapsed the spec pins the instant to noon UTC on that calendar date — a
// timezone-neutral anchor that keeps a date-only entry stable regardless of the
// user's offset. When the user expands the time field, the date+time are read as
// the browser's local wall clock and converted to UTC.

const NOON_UTC = 'T12:00:00.000Z';

// Compose a UTC ISO instant from a `yyyy-MM-dd` date and an optional `HH:mm` time.
// null time → spec default of noon UTC; provided time → browser-local wall time → UTC.
export function composeOccurredAt(date: string, time: string | null): string {
  if (time == null) return `${date}${NOON_UTC}`;
  const [h, m] = time.split(':').map(Number);
  const [y, mo, d] = date.split('-').map(Number);
  return new Date(y, mo - 1, d, h, m).toISOString();
}

// Decompose a UTC ISO instant back into the date/time fields for edit seeding.
// Collapsed (time = null) iff the instant is exactly noon-UTC on its date — the
// inverse of `composeOccurredAt(date, null)`. Otherwise the date+time are the
// browser-local wall clock, so re-composing them round-trips to the same instant.
//
// Accepted v1 limitation: noon-UTC is overloaded as the date-only sentinel, so an
// explicit time whose local wall clock happens to land exactly on 12:00 UTC (e.g. a
// UTC-0 user picking 12:00, or NY-summer 08:00) re-seeds collapsed rather than showing
// that time. This is display-only — the stored instant is unchanged and re-composing
// the collapsed date reproduces it exactly, so no value is lost. A precise fix needs a
// separate "explicit time" flag the backend doesn't carry; deferred past v1.
export function decomposeOccurredAt(utcIso: string): { date: string; time: string | null } {
  const dt = new Date(utcIso);
  const isNoonUtc =
    dt.getUTCHours() === 12 && dt.getUTCMinutes() === 0 && dt.getUTCSeconds() === 0 && dt.getUTCMilliseconds() === 0;
  if (isNoonUtc) return { date: utcIso.slice(0, 10), time: null }; // UTC date, collapsed
  const pad = (n: number) => String(n).padStart(2, '0');
  return {
    // local date + local HH:mm, expanded
    date: `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}`,
    time: `${pad(dt.getHours())}:${pad(dt.getMinutes())}`,
  };
}
