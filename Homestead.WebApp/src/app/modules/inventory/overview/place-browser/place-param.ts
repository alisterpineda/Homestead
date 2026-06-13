/**
 * The `?place=` query param on /inventory — the overview's drill-down state. Prefixes resolve
 * GUID-kind ambiguity: `area:<id>` | `item:<id>` | `unplaced`; absent or malformed = root.
 */
export type PlaceRef =
  | { kind: 'area'; id: string }
  | { kind: 'container'; id: string }
  | { kind: 'unplaced' }
  | null;

/** Parses a raw `?place=` value; anything malformed degrades to the root grid. */
export function parsePlaceParam(raw: string | null): PlaceRef {
  if (!raw) return null;
  if (raw === 'unplaced') return { kind: 'unplaced' };
  if (raw.startsWith('area:')) {
    const id = raw.slice('area:'.length);
    return id ? { kind: 'area', id } : null;
  }
  if (raw.startsWith('item:')) {
    const id = raw.slice('item:'.length);
    return id ? { kind: 'container', id } : null;
  }
  return null;
}

/** Inverse of {@link parsePlaceParam}; null clears the param (back to root). */
export function formatPlaceParam(ref: PlaceRef): string | null {
  if (!ref) return null;
  switch (ref.kind) {
    case 'area':
      return `area:${ref.id}`;
    case 'container':
      return `item:${ref.id}`;
    case 'unplaced':
      return 'unplaced';
  }
}
