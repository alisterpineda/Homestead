/** How many subject suggestions a Subject / filter box shows at most. */
export const MAX_SUGGESTIONS = 8;

/**
 * The one suggestion filter behind both the composer's Subject box and the feed's notes filter:
 * a case-insensitive substring match over the item's distinct prior subjects (brain does no
 * filtering of its own), capped so a long service log doesn't unroll a wall of options.
 */
export function matchSubjects(subjects: readonly string[], typed: string): string[] {
  const q = typed.trim().toLowerCase();
  const matches = q ? subjects.filter((s) => s.toLowerCase().includes(q)) : [...subjects];
  return matches.slice(0, MAX_SUGGESTIONS);
}
