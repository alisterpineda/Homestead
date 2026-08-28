import {
  collapseCrumbs,
  durableLocationPath,
  stockEntryLocationPath,
  locationPathText,
  renderCrumbs,
  ELLIPSIS_CRUMB,
  MAX_VISIBLE_CRUMBS,
  type LocationCrumb,
} from './location-path';
import { formatPath } from '../list/inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';

function makeItem(over: Partial<DurableResponse> = {}): DurableResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    path: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...over,
  };
}

/** The branch fixtures, reused by the anti-drift invariant at the bottom. */
const FIXTURES = {
  stored: makeItem({
    resolvedAreaId: 'garage',
    path: [
      { id: 'garage', name: 'Garage', kind: 'Area' },
      { id: 'chest', name: 'Tool chest', kind: 'Container' },
    ],
  }),
  component: makeItem({
    parentIsContainer: false,
    resolvedAreaId: 'garage',
    path: [
      { id: 'garage', name: 'Garage', kind: 'Area' },
      { id: 'pc', name: 'Gaming PC', kind: 'Item' },
    ],
  }),
  unplaced: makeItem(),
  unplacedChain: makeItem({
    resolvedAreaId: null,
    path: [{ id: 'bin', name: 'Floating bin', kind: 'Container' }],
  }),
  // parentIsContainer is false but the path is somehow empty — falls through to storage rendering.
  componentWithoutParent: makeItem({ parentIsContainer: false, path: [] }),
} satisfies Record<string, DurableResponse>;

describe('durableLocationPath', () => {
  it('maps a storage path to one crumb per segment', () => {
    expect(durableLocationPath(FIXTURES.stored)).toEqual({
      prefix: null,
      crumbs: [
        { label: 'Garage', muted: false },
        { label: 'Tool chest', muted: false },
      ],
    });
  });

  it('reads a component as composition — "Part of" plus the assembly', () => {
    const view = durableLocationPath(FIXTURES.component);
    expect(view.prefix).toBe('Part of');
    // Only the assembly, not the storage chain above it.
    expect(view.crumbs).toEqual([{ label: 'Gaming PC', muted: false }]);
  });

  it('renders a directly-unplaced item as a single muted crumb', () => {
    expect(durableLocationPath(FIXTURES.unplaced)).toEqual({
      prefix: null,
      crumbs: [{ label: 'Unplaced', muted: true }],
    });
  });

  it('prefixes a chain rooted at an unplaced container with the muted Unplaced crumb', () => {
    const view = durableLocationPath(FIXTURES.unplacedChain);
    expect(view.crumbs.map(c => c.label)).toEqual(['Unplaced', 'Floating bin']);
    expect(view.crumbs.map(c => c.muted)).toEqual([true, false]);
  });

  it('falls through to storage rendering when a component has no parent segment', () => {
    const view = durableLocationPath(FIXTURES.componentWithoutParent);
    expect(view.prefix).toBeNull();
    expect(view.crumbs.map(c => c.label)).toEqual(['Unplaced']);
  });
});

describe('stockEntryLocationPath', () => {
  function makeEntry(over: Partial<StockEntryResponse> = {}): StockEntryResponse {
    return { id: 'stock-1', quantity: 3, path: [], ...over };
  }

  it('maps each holding-location segment to a crumb', () => {
    const entry = makeEntry({
      locationLabel: 'Garage › Tool chest',
      path: [
        { id: 'garage', name: 'Garage', kind: 'Area' },
        { id: 'chest', name: 'Tool chest', kind: 'Container' },
      ],
    });

    const view = stockEntryLocationPath(entry);
    expect(view.crumbs.map(c => c.label)).toEqual(['Garage', 'Tool chest']);
    // The rendered text is byte-identical to the API's own label — no re-wording.
    expect(locationPathText(view)).toBe(entry.locationLabel);
  });

  it('renders an unresolved entry as a muted "(unknown)"', () => {
    expect(stockEntryLocationPath(makeEntry())).toEqual({
      prefix: null,
      crumbs: [{ label: '(unknown)', muted: true }],
    });
  });
});

describe('collapseCrumbs', () => {
  function crumbs(...labels: string[]): LocationCrumb[] {
    return labels.map(label => ({ label, muted: false }));
  }

  it.each([1, 2, MAX_VISIBLE_CRUMBS])('returns %i crumbs untouched', (n) => {
    const input = crumbs(...Array.from({ length: n }, (_, i) => `Seg ${i}`));
    expect(collapseCrumbs(input)).toEqual(input);
  });

  it('collapses the middle of a four-segment path to root › … › leaf', () => {
    const input = crumbs('A', 'B', 'C', 'D');
    expect(collapseCrumbs(input)).toEqual([input[0], ELLIPSIS_CRUMB, input[3]]);
  });

  it('keeps the same three-entry shape however deep the path goes', () => {
    const input = crumbs('A', 'B', 'C', 'D', 'E', 'F');
    expect(collapseCrumbs(input)).toEqual([input[0], ELLIPSIS_CRUMB, input[5]]);
  });

  it('keeps the synthetic Unplaced root as the visible root of a collapsed chain', () => {
    const view = durableLocationPath(makeItem({
      resolvedAreaId: null,
      path: [
        { id: 'a', name: 'A', kind: 'Container' },
        { id: 'b', name: 'B', kind: 'Container' },
        { id: 'c', name: 'C', kind: 'Container' },
      ],
    }));

    const visible = collapseCrumbs(view.crumbs);
    expect(visible.map(c => c.label)).toEqual(['Unplaced', '…', 'C']);
    expect(visible[0].muted).toBe(true);
  });
});

describe('renderCrumbs', () => {
  function crumbs(...labels: string[]): LocationCrumb[] {
    return labels.map(label => ({ label, muted: false }));
  }

  it('roles an uncollapsed path root › middle › leaf, muting everything but the leaf', () => {
    const rendered = renderCrumbs(crumbs('Garage', 'Tool chest', 'Bin'));
    expect(rendered.map(c => c.role)).toEqual(['root', 'middle', 'leaf']);
    expect(rendered.map(c => c.muted)).toEqual([true, true, false]);
  });

  it('shows every segment when collapse is off, muting all but the leaf', () => {
    const rendered = renderCrumbs(crumbs('A', 'B', 'C', 'D'), false);
    expect(rendered.map(c => c.label)).toEqual(['A', 'B', 'C', 'D']);
    expect(rendered.map(c => c.role)).toEqual(['root', 'middle', 'middle', 'leaf']);
    expect(rendered.map(c => c.muted)).toEqual([true, true, true, false]);
  });

  it('collapses before it lays out, so the hidden middle becomes one ellipsis segment', () => {
    const rendered = renderCrumbs(crumbs('A', 'B', 'C', 'D'));
    expect(rendered.map(c => c.role)).toEqual(['root', 'ellipsis', 'leaf']);
    expect(rendered.map(c => c.label)).toEqual(['A', '…', 'D']);
  });

  it('treats a lone crumb as the leaf, muted only if the crumb itself was', () => {
    expect(renderCrumbs(crumbs('Gaming PC'))).toEqual([
      { label: 'Gaming PC', muted: false, role: 'leaf' },
    ]);
    expect(renderCrumbs([{ label: 'Unplaced', muted: true }])).toEqual([
      { label: 'Unplaced', muted: true, role: 'leaf' },
    ]);
  });

  it('keeps the Unplaced root of a collapsed chain muted and the leaf at the caller colour', () => {
    const view = durableLocationPath(makeItem({
      resolvedAreaId: null,
      path: [
        { id: 'a', name: 'A', kind: 'Container' },
        { id: 'b', name: 'B', kind: 'Container' },
        { id: 'c', name: 'C', kind: 'Container' },
      ],
    }));

    const rendered = renderCrumbs(view.crumbs);
    expect(rendered.map(c => c.label)).toEqual(['Unplaced', '…', 'C']);
    expect(rendered.map(c => c.muted)).toEqual([true, true, false]);
    expect(rendered[0].role).toBe('root');
  });
});

describe('locationPathText', () => {
  it('joins crumbs with the breadcrumb separator', () => {
    expect(locationPathText(durableLocationPath(FIXTURES.stored))).toBe('Garage › Tool chest');
  });

  it('puts the prefix ahead of the crumbs', () => {
    expect(locationPathText(durableLocationPath(FIXTURES.component))).toBe('Part of Gaming PC');
  });

  it('never collapses — it is the sort key and the tooltip that reveals what the screen hid', () => {
    const view = durableLocationPath(makeItem({
      resolvedAreaId: 'house',
      path: [
        { id: 'house', name: 'House', kind: 'Area' },
        { id: 'basement', name: 'Basement', kind: 'Area' },
        { id: 'chest', name: 'Chest', kind: 'Container' },
        { id: 'drawer', name: 'Drawer', kind: 'Container' },
        { id: 'bin', name: 'Bin', kind: 'Container' },
      ],
    }));

    expect(locationPathText(view)).toBe('House › Basement › Chest › Drawer › Bin');
  });

  // The anti-drift guard for the refactor: formatPath is now a flattening of this view model, so the
  // sort key / accessorFn / tooltip text can never disagree with the crumbs the surfaces render.
  it.each(Object.entries(FIXTURES))('agrees with formatPath for the %s fixture', (_key, durable) => {
    expect(locationPathText(durableLocationPath(durable))).toBe(formatPath(durable));
  });
});
