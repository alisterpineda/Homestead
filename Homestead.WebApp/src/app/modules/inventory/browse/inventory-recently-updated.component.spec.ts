import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { InventoryRecentlyUpdatedComponent } from './inventory-recently-updated.component';
import type { RecentlyUpdatedItem } from '@/api/generated/models/recently-updated-item';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

function makeDurable(over: Partial<DurableResponse> = {}): DurableResponse {
  return {
    id: 'd1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    durableKind: 'Plain',
    path: [],
    createdAt: '',
    updatedAt: '',
    ...over,
  };
}

function makeConsumable(over: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 'c1',
    name: 'Wood screws',
    status: 'Active',
    unit: 'pcs',
    totalOnHand: 12,
    isLow: false,
    createdAt: '',
    updatedAt: '',
    stockLocations: [],
    ...over,
  };
}

// A timestamp two hours ago so formatRelativeAge yields the stable "2h ago".
const TWO_HOURS_AGO = new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString();

describe('InventoryRecentlyUpdatedComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
      ],
    });
  });

  async function create(items: RecentlyUpdatedItem[]): Promise<ComponentFixture<InventoryRecentlyUpdatedComponent>> {
    const fixture = TestBed.createComponent(InventoryRecentlyUpdatedComponent);
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function buttons(fixture: ComponentFixture<InventoryRecentlyUpdatedComponent>): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
  }

  function text(fixture: ComponentFixture<InventoryRecentlyUpdatedComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders nothing when the strip is empty', async () => {
    const fixture = await create([]);
    expect(text(fixture).trim()).toBe('');
    expect(buttons(fixture)).toHaveLength(0);
  });

  it('renders one chip per entry in the given order, durable then consumable', async () => {
    const fixture = await create([
      { lastActivityAt: TWO_HOURS_AGO, durable: makeDurable({ name: 'Cordless drill' }) },
      { lastActivityAt: TWO_HOURS_AGO, consumable: makeConsumable({ name: 'Wood screws' }) },
    ]);

    expect(text(fixture)).toContain('Recently updated');
    const chips = buttons(fixture);
    expect(chips).toHaveLength(2);
    // Order is preserved exactly — the backend already sorted by recency.
    expect(chips[0].textContent).toContain('Cordless drill');
    expect(chips[1].textContent).toContain('Wood screws');
  });

  it('renders a durable chip with its status badge and relative age', async () => {
    const fixture = await create([
      { lastActivityAt: TWO_HOURS_AGO, durable: makeDurable({ name: 'Lent ladder', status: 'Lent' }) },
    ]);

    const chip = buttons(fixture)[0];
    expect(chip.textContent).toContain('Lent ladder');
    expect(chip.textContent).toContain('Lent'); // status badge
    expect(chip.textContent).toContain('2h ago');
  });

  it('renders a consumable chip with on-hand quantity, unit, and a low badge when low', async () => {
    const fixture = await create([
      { lastActivityAt: TWO_HOURS_AGO, consumable: makeConsumable({ name: 'Glue', unit: 'tubes', totalOnHand: 2, isLow: true }) },
    ]);

    const chip = buttons(fixture)[0];
    expect(chip.textContent).toContain('Glue');
    expect(chip.textContent).toContain('2 tubes');
    expect(chip.textContent).toContain('Low');
    expect(chip.textContent).toContain('2h ago');
  });

  it('omits the low badge for a consumable that is not low', async () => {
    const fixture = await create([
      { lastActivityAt: TWO_HOURS_AGO, consumable: makeConsumable({ name: 'Wood screws', isLow: false }) },
    ]);

    expect(buttons(fixture)[0].textContent).not.toContain('Low');
  });

  it('emits the full durable object on click', async () => {
    const durable = makeDurable({ id: 'd9', name: 'Cordless drill' });
    const fixture = await create([{ lastActivityAt: TWO_HOURS_AGO, durable }]);

    let emitted: DurableResponse | undefined;
    fixture.componentInstance.openDurable.subscribe(d => (emitted = d));
    buttons(fixture)[0].click();

    expect(emitted).toEqual(durable);
  });

  it('emits the full consumable object on click', async () => {
    const consumable = makeConsumable({ id: 'c9', name: 'Wood screws' });
    const fixture = await create([{ lastActivityAt: TWO_HOURS_AGO, consumable }]);

    let emitted: ConsumableResponse | undefined;
    fixture.componentInstance.openConsumable.subscribe(c => (emitted = c));
    buttons(fixture)[0].click();

    expect(emitted).toEqual(consumable);
  });

  it('renders a durable row with its resolved location', async () => {
    const fixture = await create([
      {
        lastActivityAt: TWO_HOURS_AGO,
        durable: makeDurable({
          name: 'Cordless drill',
          resolvedAreaId: 'garage',
          path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
        }),
      },
    ]);

    // The location (hidden on narrow viewports) is still in the jsdom textContent.
    expect(buttons(fixture)[0].textContent).toContain('Garage');
  });

  it('renders a consumable row with its stock location', async () => {
    const fixture = await create([
      {
        lastActivityAt: TWO_HOURS_AGO,
        consumable: makeConsumable({
          name: 'Wood screws',
          stockLocations: [{
            id: 'e1', quantity: 12, areaId: 'pantry',
            path: [{ id: 'pantry', name: 'Pantry', kind: 'Area' }], locationLabel: 'Pantry',
          }],
        }),
      },
    ]);

    expect(buttons(fixture)[0].textContent).toContain('Pantry');
  });

  it('links "View all" to the items list sorted by Updated-descending', async () => {
    const fixture = await create([
      { lastActivityAt: TWO_HOURS_AGO, durable: makeDurable() },
    ]);

    const link = (fixture.nativeElement as HTMLElement).querySelector('a');
    const href = link?.getAttribute('href') ?? '';
    expect(href).toContain('/inventory/items');
    expect(href).toContain('sort=updatedAt');
    expect(href).toContain('dir=desc');
  });
});
