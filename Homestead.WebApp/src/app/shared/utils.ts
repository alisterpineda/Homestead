export function formatBytes(bytes: number, decimals = 1): string {
  if (bytes === 0) return '0 B';

  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  const size = sizes[i] ?? 'TB';

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(decimals))} ${size}`;
}

/** Parse an ISO date string (YYYY-MM-DD) to a local Date, avoiding UTC timezone shift. */
export function parseDate(isoString: string | null | undefined): Date | null {
  if (!isoString) return null;
  const parts = isoString.substring(0, 10).split('-');
  return new Date(+parts[0], +parts[1] - 1, +parts[2]);
}

/** Format a Date as an ISO date-only string (YYYY-MM-DD). */
export function toIsoDateString(date: Date | null): string | null {
  if (!date) return null;
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Handle a file input change event: pass selected files to a callback and reset the input. */
export function handleFileInputChange(event: Event, onFiles: (files: File[]) => void): void {
  const input = event.target as HTMLInputElement;
  const files = input.files;
  if (files && files.length > 0) {
    onFiles(Array.from(files));
  }
  input.value = '';
}

/** Compare two Dates by year/month/day only (ignoring time). */
export function datesEqual(a: Date | null, b: Date | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}
