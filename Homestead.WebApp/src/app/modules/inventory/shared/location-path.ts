import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';

/** One step of a rendered location path. */
export interface LocationCrumb {
  readonly label: string;
  /** Rendered muted — the synthetic "Unplaced" root, an unresolved stock location, or the collapse ellipsis. */
  readonly muted: boolean;
}

/** A location path ready to render: an optional literal prefix, then the crumbs. */
export interface LocationPathView {
  /** Literal text before the crumbs — 'Part of' for a component. Null for a storage path. */
  readonly prefix: string | null;
  readonly crumbs: readonly LocationCrumb[];
}

/** The crumb standing in for hidden middle segments. Compared by identity (`c === ELLIPSIS_CRUMB`). */
export const ELLIPSIS_CRUMB: LocationCrumb = { label: '…', muted: true };

/** Segment count above which the middle collapses. Root › … › leaf is always three visible entries. */
export const MAX_VISIBLE_CRUMBS = 3;

/**
 * Display form of a path: at most MAX_VISIBLE_CRUMBS entries. Four or more segments collapse to
 * root › … › leaf — root and leaf are the two that orient ("which building" / "which shelf"); the
 * middle lives in the tooltip. Never used for sort/search text — see locationPathText.
 */
export function collapseCrumbs(crumbs: readonly LocationCrumb[]): readonly LocationCrumb[] {
  if (crumbs.length <= MAX_VISIBLE_CRUMBS) return crumbs;
  return [crumbs[0], ELLIPSIS_CRUMB, crumbs[crumbs.length - 1]];
}

export type CrumbRole = 'leaf' | 'root' | 'middle' | 'ellipsis';

/**
 * One on-screen segment with its layout decided: what it says, whether it's muted, and how it
 * yields width.
 */
export interface RenderedCrumb {
  readonly label: string;
  /**
   * text-muted-foreground: every ancestor, the collapse ellipsis, and a muted leaf
   * (Unplaced / (unknown)).
   */
  readonly muted: boolean;
  readonly role: CrumbRole;
}

/**
 * Collapse + layout in one pass, shared by the component and the Items list cell. Width priority is
 * leaf > root > middle: the leaf is what you're looking for, the root is where; the middle is what
 * the tooltip is for. The ellipsis never shrinks. A single crumb is the leaf.
 *
 * `collapse` is a per-surface choice, not a per-path one: the depth rule exists for surfaces that
 * are narrow by construction (the quick-panel sheet, the list column). A full-width detail page has
 * room for every segment, so it passes `false` and relies on per-segment truncation alone.
 */
export function renderCrumbs(
  crumbs: readonly LocationCrumb[],
  collapse = true,
): readonly RenderedCrumb[] {
  const visible = collapse ? collapseCrumbs(crumbs) : crumbs;
  return visible.map((crumb, i) => {
    const role: CrumbRole =
      i === visible.length - 1
        ? 'leaf'
        : crumb === ELLIPSIS_CRUMB
          ? 'ellipsis'
          : i === 0
            ? 'root'
            : 'middle';
    return { label: crumb.label, muted: crumb.muted || role !== 'leaf', role };
  });
}

/**
 * Flex-shrink weights ~100× apart so the browser squeezes the middle, then the root, before
 * touching the leaf. Root and middle keep small floors so a squeezed segment still shows its
 * first letters (`QA U…`) rather than vanishing into a run of bare chevrons.
 */
export const CRUMB_CLASSES: Record<CrumbRole, string> = {
  leaf: 'min-w-0 max-w-full shrink truncate',
  root: 'min-w-[5ch] shrink-[100] truncate',
  middle: 'min-w-[4ch] shrink-[10000] truncate',
  ellipsis: 'shrink-0',
};

/**
 * The line around the crumbs, shared verbatim by the component and the Items list cell so the
 * two renderers cannot drift: the host row, the `Part of` literal, and the separator glyph
 * (padded with non-breaking spaces, since template whitespace around it would be collapsed away).
 */
export const PATH_HOST_CLASSES = 'flex min-w-0 whitespace-nowrap';
export const PATH_PREFIX_CLASSES = 'shrink-0 text-muted-foreground';
export const PATH_SEPARATOR_CLASSES = 'shrink-0 text-muted-foreground';
export const PATH_SEPARATOR = '\u00a0›\u00a0';

const UNPLACED_CRUMB: LocationCrumb = { label: 'Unplaced', muted: true };

/**
 * The single source of truth for how a durable's resolved location reads. A directly-unplaced
 * durable, or one whose chain resolves to Unplaced, is prefixed with the Unplaced bucket; a
 * component (child of a non-container) reads as composition, not storage: "Part of {parent}".
 * {@link locationPathText} flattens this back to the plain text every surface uses for sorting,
 * search and tooltips, so the rule lives here once rather than once per surface.
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
 * so rendering from `path` reproduces the same text; an entry whose chain resolved to nothing keeps
 * today's muted "(unknown)" fallback.
 */
export function stockEntryLocationPath(entry: StockEntryResponse): LocationPathView {
  const path = entry.path ?? [];
  if (path.length === 0) {
    return { prefix: null, crumbs: [{ label: '(unknown)', muted: true }] };
  }
  return { prefix: null, crumbs: path.map(toCrumb) };
}

/**
 * Flattens a view back to plain text ("Garage › Cupboard › Bin") for sorting, tooltips, and search.
 * Never collapsed — this is the full path, which is exactly what a tooltip over a collapsed
 * rendering has to reveal.
 */
export function locationPathText(view: LocationPathView): string {
  const labels = view.crumbs.map(c => c.label).join(' › ');
  return view.prefix ? `${view.prefix} ${labels}` : labels;
}

function toCrumb(seg: LocationSegmentResponse): LocationCrumb {
  return { label: seg.name, muted: false };
}
