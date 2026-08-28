import {
  durableLocationPath,
  stockEntryLocationPath,
  locationPathText,
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
  it('maps a storage path to one crumb per segment, each linked by kind', () => {
    expect(durableLocationPath(FIXTURES.stored)).toEqual({
      prefix: null,
      crumbs: [
        { label: 'Garage', link: ['/inventory/browse/area', 'garage'], muted: false },
        { label: 'Tool chest', link: ['/inventory/browse/container', 'chest'], muted: false },
      ],
    });
  });

  it('reads a component as composition — "Part of" plus the assembly, linked to its detail page', () => {
    const view = durableLocationPath(FIXTURES.component);
    expect(view.prefix).toBe('Part of');
    // Only the assembly, not the storage chain above it — and an Item segment is not a place.
    expect(view.crumbs).toEqual([
      { label: 'Gaming PC', link: ['/inventory/items', 'pc'], muted: false },
    ]);
  });

  it('renders a directly-unplaced item as a single muted crumb linking to the unplaced bucket', () => {
    expect(durableLocationPath(FIXTURES.unplaced)).toEqual({
      prefix: null,
      crumbs: [{ label: 'Unplaced', link: ['/inventory/browse/unplaced'], muted: true }],
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

  it('maps each holding-location segment through its kind', () => {
    const entry = makeEntry({
      locationLabel: 'Garage › Tool chest',
      path: [
        { id: 'garage', name: 'Garage', kind: 'Area' },
        { id: 'chest', name: 'Tool chest', kind: 'Container' },
      ],
    });

    const view = stockEntryLocationPath(entry);
    expect(view.crumbs.map(c => c.link)).toEqual([
      ['/inventory/browse/area', 'garage'],
      ['/inventory/browse/container', 'chest'],
    ]);
    // The rendered text is byte-identical to the API's own label — links, no re-wording.
    expect(locationPathText(view)).toBe(entry.locationLabel);
  });

  it('renders an unresolved entry as a muted, link-less "(unknown)"', () => {
    expect(stockEntryLocationPath(makeEntry())).toEqual({
      prefix: null,
      crumbs: [{ label: '(unknown)', link: null, muted: true }],
    });
  });
});

describe('locationPathText', () => {
  it('joins crumbs with the breadcrumb separator', () => {
    expect(locationPathText(durableLocationPath(FIXTURES.stored))).toBe('Garage › Tool chest');
  });

  it('puts the prefix ahead of the crumbs', () => {
    expect(locationPathText(durableLocationPath(FIXTURES.component))).toBe('Part of Gaming PC');
  });

  // The anti-drift guard for the refactor: formatPath is now a flattening of this view model, so the
  // sort key / accessorFn / tooltip text can never disagree with what the linked surfaces render.
  it.each(Object.entries(FIXTURES))('agrees with formatPath for the %s fixture', (_key, durable) => {
    expect(locationPathText(durableLocationPath(durable))).toBe(formatPath(durable));
  });
});
