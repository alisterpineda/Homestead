import {
  durableToRow,
  consumableToRow,
  placeConsumableToRow,
  formatPath,
  sortRows,
  type InventoryRow,
} from './inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { PlaceConsumableResponse } from '@/api/generated/models/place-consumable-response';
import type { ItemTagDto } from '@/api/generated/models/item-tag-dto';

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

function makeConsumable(over: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 'consumable-1',
    name: 'Paper towels',
    status: 'Active',
    unit: 'rolls',
    totalOnHand: 12,
    isLow: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    stockLocations: [],
    ...over,
  };
}

describe('formatPath', () => {
  it('renders a storage path for a stored item', () => {
    const it = makeItem({
      resolvedAreaId: 'garage',
      path: [
        { id: 'garage', name: 'Garage', kind: 'Area' },
        { id: 'chest', name: 'Tool chest', kind: 'Container' },
      ],
    });
    expect(formatPath(it)).toBe('Garage › Tool chest');
  });

  it('renders "Part of {assembly}" for a component', () => {
    const it = makeItem({
      parentIsContainer: false,
      resolvedAreaId: 'garage',
      path: [
        { id: 'garage', name: 'Garage', kind: 'Area' },
        // 'Item', not 'Container': parentIsContainer false means the resolver types this same row
        // as a non-container assembly. 'Container' here is a shape the backend cannot emit.
        { id: 'pc', name: 'Gaming PC', kind: 'Item' },
      ],
    });
    expect(formatPath(it)).toBe('Part of Gaming PC');
  });

  it('renders Unplaced for a directly-unplaced item', () => {
    expect(formatPath(makeItem())).toBe('Unplaced');
  });
});

describe('placeConsumableToRow', () => {
  it('carries the place-local quantity with an of-total hint when more sits elsewhere', () => {
    const pc: PlaceConsumableResponse = {
      consumable: makeConsumable({ id: 'screws', name: 'Wood screws', totalOnHand: 10, unit: 'pcs', isLow: true }),
      localQuantity: 4,
    };
    const row = placeConsumableToRow(pc);
    expect(row.kind).toBe('consumable');
    expect(row.id).toBe('screws');
    expect(row.name).toBe('Wood screws');
    expect(row.unit).toBe('pcs');
    expect(row.isLow).toBe(true);
    expect(row.onHand).toBe(4);
    expect(row.onHandHint).toBe('of 10 total');
    expect(row.consumable).toBe(pc.consumable);
  });

  it('omits the of-total hint when the local quantity is the whole stock', () => {
    const row = placeConsumableToRow({
      consumable: makeConsumable({ totalOnHand: 7 }),
      localQuantity: 7,
    });
    expect(row.onHand).toBe(7);
    expect(row.onHandHint).toBeUndefined();
  });
});

describe('tag mapping', () => {
  const tag = {
    id: 'g1', name: 'Cordless', path: 'Tools/Cordless', color: null, effectiveColor: '#3b82f6',
  } satisfies ItemTagDto;

  it('carries a durable\'s tags through, defaulting undefined to an empty array', () => {
    expect(durableToRow(makeItem({ tags: [tag] })).tags).toEqual([tag]);
    expect(durableToRow(makeItem({})).tags).toEqual([]);
  });

  it('carries a consumable\'s tags through, defaulting undefined to an empty array', () => {
    expect(consumableToRow(makeConsumable({ tags: [tag] })).tags).toEqual([tag]);
    expect(consumableToRow(makeConsumable({})).tags).toEqual([]);
  });

  it('inherits the consumable\'s tags on a place-scoped row', () => {
    const row = placeConsumableToRow({ consumable: makeConsumable({ tags: [tag] }), localQuantity: 1 });
    expect(row.tags).toEqual([tag]);
  });
});

describe('sortRows', () => {
  const a = durableToRow(makeItem({ id: 'a', name: 'Anvil', status: 'Active', updatedAt: '2026-01-01T00:00:00Z' }));
  const z = durableToRow(makeItem({ id: 'z', name: 'Zither', status: 'Lent', updatedAt: '2026-03-01T00:00:00Z' }));
  const lowC = consumableToRow(makeConsumable({ id: 'c', name: 'Mid', totalOnHand: 3, updatedAt: '2026-02-01T00:00:00Z' }));

  function ids(rows: readonly InventoryRow[]): string[] {
    return rows.map(r => r.id);
  }

  it('sorts by name ascending and descending', () => {
    expect(ids(sortRows([z, a, lowC], { id: 'name', desc: false }))).toEqual(['a', 'c', 'z']);
    expect(ids(sortRows([a, lowC, z], { id: 'name', desc: true }))).toEqual(['z', 'c', 'a']);
  });

  it('sorts by status label', () => {
    // Labels: Active < Lent < Mid(consumable Active). Active rows tie, Lent sorts after.
    const result = sortRows([z, a], { id: 'status', desc: false });
    expect(result[0].status).toBe('Active');
    expect(result[1].status).toBe('Lent');
  });

  it('sorts durables (null onHand) last regardless of direction', () => {
    expect(ids(sortRows([a, lowC, z], { id: 'onHand', desc: false })).slice(0, 1)).toEqual(['c']);
    // Descending still parks the null-quantity durables after the consumable.
    const desc = sortRows([a, lowC, z], { id: 'onHand', desc: true });
    expect(desc[0].id).toBe('c');
    expect(desc.slice(1).every(r => r.onHand === null)).toBe(true);
  });

  it('sorts by updatedAt with newest first when descending', () => {
    expect(ids(sortRows([a, z, lowC], { id: 'updatedAt', desc: true }))).toEqual(['z', 'c', 'a']);
  });

  it('returns a new array without mutating the input', () => {
    const input = [z, a];
    const sorted = sortRows(input, { id: 'name', desc: false });
    expect(sorted).not.toBe(input);
    expect(ids(input)).toEqual(['z', 'a']);
  });
});
