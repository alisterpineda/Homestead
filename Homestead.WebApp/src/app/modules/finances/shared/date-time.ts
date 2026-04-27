export function localStartOfToday(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d;
}

export function startOfDayLocal(date: Date): Date {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  return d;
}

export function timeHhMmLocal(date: Date): string {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

export function currentTimeHhMm(): string {
  return timeHhMmLocal(new Date());
}

export function combineDateAndTimeToIso(date: Date, timeHhMm: string): string | null {
  if (!timeHhMm) return null;
  const [hours, minutes] = timeHhMm.split(':').map(Number);
  if (!Number.isFinite(hours) || !Number.isFinite(minutes)) return null;
  const combined = new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate(),
    hours,
    minutes,
    0,
    0,
  );
  return combined.toISOString();
}
