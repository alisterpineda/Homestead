/**
 * Whether a note body is long enough to clamp behind a "Show more" toggle. A forty-note service
 * log has to stay scannable, so anything past a short paragraph collapses to three lines.
 *
 * This is a *content* heuristic, not a measurement: `scrollHeight` is unreliable in jsdom (so the
 * behaviour would be untestable) and reading it needs a `ResizeObserver` per row for very little
 * gain. The two thresholds mirror the `line-clamp-3` the template applies — three explicit lines,
 * or enough characters that three rendered lines are near-certain at any realistic column width.
 */
const MAX_UNCLAMPED_LINES = 3;
const MAX_UNCLAMPED_CHARS = 240;

export function needsClamp(body: string | null | undefined): boolean {
  if (!body) return false;
  if (body.length > MAX_UNCLAMPED_CHARS) return true;
  return body.split('\n').length > MAX_UNCLAMPED_LINES;
}
