// A timestamp a little ahead of the client clock is almost always client/server skew on a
// just-touched item, not a real future date — render it "just now" (it falls through to the
// seconds branch below) rather than a jarring full date. Only timestamps well beyond the skew
// window are treated as genuinely future and shown as a locale date.
const SKEW_TOLERANCE_MS = 60_000;

export function formatRelativeAge(timestamp: string): string {
  const now = Date.now();
  const then = new Date(timestamp).getTime();
  // An unparseable timestamp yields NaN, and every comparison below would be false, falling through
  // to a nonsensical "NaNy ago". Guard it: render nothing rather than a broken string.
  if (Number.isNaN(then)) return '';
  const diffMs = now - then;
  if (diffMs < -SKEW_TOLERANCE_MS) return new Date(timestamp).toLocaleDateString();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 60) return 'just now';
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 30) return `${diffDay}d ago`;
  const diffMo = Math.floor(diffDay / 30);
  if (diffMo < 12) return `${diffMo}mo ago`;
  // Derive years from the same 30-day month unit, not /365: mixing the two left a gap at
  // diffDay 360–364 (diffMo == 12 so the months branch is skipped, yet diffDay/365 == 0),
  // which rendered a nonsensical "0y ago".
  const diffYr = Math.floor(diffMo / 12);
  return `${diffYr}y ago`;
}
