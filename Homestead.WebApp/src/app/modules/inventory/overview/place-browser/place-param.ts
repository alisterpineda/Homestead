import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

/**
 * A browse-by-place drill target. Areas and Unplaced live in `/inventory/browse/...` routes (one
 * per node); a container drills into its canonical detail page at `/inventory/items/:id`. The
 * ancestor trail comes from the API's `PlaceContentsResponse.path`. `null` is the root (area grid).
 */
export type PlaceRef =
  | { kind: 'area'; id: string }
  | { kind: 'container'; id: string }
  | { kind: 'unplaced' }
  | null;

/**
 * The router link array for a place. Areas/Unplaced drill within the browse surface; a container
 * routes to its detail page (`/inventory/items/:id`) — the canonical "thing + its contents" surface
 * — so every container place-card and breadcrumb segment lands there rather than the old browse drill.
 */
export function placeRefToLink(ref: PlaceRef): unknown[] {
  if (!ref) return ['/inventory/browse'];
  switch (ref.kind) {
    case 'area':
      return ['/inventory/browse/area', ref.id];
    case 'container':
      return ['/inventory/items', ref.id];
    case 'unplaced':
      return ['/inventory/browse/unplaced'];
  }
}

/** A breadcrumb segment's drill target comes from its KIND, never its position in the path. */
export function segmentToRef(seg: LocationSegmentResponse): PlaceRef {
  return seg.kind === 'Area'
    ? { kind: 'area', id: seg.id }
    : { kind: 'container', id: seg.id };
}

/** A stable identity string for a PlaceRef — used to tell a place change from a same-place refresh. */
export function placeKey(ref: PlaceRef): string | null {
  if (!ref) return null;
  return ref.kind === 'unplaced' ? 'unplaced' : `${ref.kind}:${ref.id}`;
}

/**
 * The display name of a drilled place: 'Unplaced', else the last segment of the resolved path,
 * else the drilled container's own name (path not yet loaded). '' at the root.
 */
export function placeDisplayName(ref: PlaceRef, contents: PlaceContentsResponse | null): string {
  if (!ref) return '';
  if (ref.kind === 'unplaced') return 'Unplaced';
  const path = contents?.path ?? [];
  return path.length > 0 ? path[path.length - 1].name : (contents?.containerItem?.name ?? '');
}
