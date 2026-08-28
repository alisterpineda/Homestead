import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import { segmentToLink } from '../overview/place-browser/place-param';

/** One step of a rendered location path. */
export interface LocationCrumb {
  readonly label: string;
  /** RouterLink commands, or null for a non-navigable label. */
  readonly link: readonly unknown[] | null;
  /** Rendered muted — the synthetic "Unplaced" root, or an unresolved stock location. */
  readonly muted: boolean;
}

/** A location path ready to render: an optional literal prefix, then the crumbs. */
export interface LocationPathView {
  /** Literal text before the crumbs — 'Part of' for a component. Null for a storage path. */
  readonly prefix: string | null;
  readonly crumbs: readonly LocationCrumb[];
}

const UNPLACED_CRUMB: LocationCrumb = {
  label: 'Unplaced',
  link: ['/inventory/browse/unplaced'],
  muted: true,
};

/**
 * The single source of truth for how a durable's resolved location reads. A directly-unplaced
 * durable, or one whose chain resolves to Unplaced, is prefixed with the Unplaced bucket; a
 * component (child of a non-container) reads as composition, not storage: "Part of {parent}".
 * {@link locationPathText} flattens this back to the plain text every non-linked surface uses, so
 * the rule lives here once rather than once per surface.
 */
export function durableLocationPath(durable: DurableResponse): LocationPathView {
  const path = durable.path ?? [];
  // A component's immediate parent (the non-container) is the last path segment; fall through to the
  // storage rendering only if it's somehow missing, rather than emitting a placeholder.
  const parent = path.at(-1);
  if (durable.parentIsContainer === false && parent) {
    return { prefix: 'Part of', crumbs: [toCrumb(parent)] };
  }
  if (path.length === 0) {
    return { prefix: null, crumbs: [UNPLACED_CRUMB] };
  }
  const crumbs = path.map(toCrumb);
  return {
    prefix: null,
    // A chain rooted at an unplaced container has no area segment — flag that visually.
    crumbs: durable.resolvedAreaId == null ? [UNPLACED_CRUMB, ...crumbs] : crumbs,
  };
}

/**
 * One stock entry's holding-location path. The API's `locationLabel` is exactly these names joined,
 * so rendering from `path` is the same text made navigable; an entry whose chain resolved to nothing
 * keeps today's muted "(unknown)" fallback.
 */
export function stockEntryLocationPath(entry: StockEntryResponse): LocationPathView {
  const path = entry.path ?? [];
  if (path.length === 0) {
    return { prefix: null, crumbs: [{ label: '(unknown)', link: null, muted: true }] };
  }
  return { prefix: null, crumbs: path.map(toCrumb) };
}

/** Flattens a view back to plain text ("Garage › Cupboard › Bin") for sorting, tooltips, and search. */
export function locationPathText(view: LocationPathView): string {
  const labels = view.crumbs.map(c => c.label).join(' › ');
  return view.prefix ? `${view.prefix} ${labels}` : labels;
}

function toCrumb(seg: LocationSegmentResponse): LocationCrumb {
  return { label: seg.name, link: segmentToLink(seg), muted: false };
}
