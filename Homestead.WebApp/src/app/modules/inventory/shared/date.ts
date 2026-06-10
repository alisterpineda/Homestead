/** `yyyy-MM-dd` for tomorrow (UTC) — the max selectable acquired-on date (today + 1 day slack). */
export function tomorrowIso(): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}
