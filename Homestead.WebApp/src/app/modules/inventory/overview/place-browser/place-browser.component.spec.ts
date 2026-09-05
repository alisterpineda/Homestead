import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { PlaceBrowserComponent } from './place-browser.component';
import { InventoryItemsViewComponent } from '../../list/inventory-items-view.component';
import type { PlaceRef } from './place-param';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

// The embedded create dialog hosts pickers that use ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(over: Partial<DurableResponse>): DurableResponse {
  return {
    id: 'x',
    name: 'Item',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    path: [],
    createdAt: '',
    updatedAt: '',
    ...over,
  };
}

function makeConsumable(over: Partial<ConsumableResponse>): ConsumableResponse {
  return {
    id: 's',
    name: 'Consumable',
    status: 'Active',
    unit: 'pcs',
    totalOnHand: 0,
    isLow: false,
    createdAt: '',
    updatedAt: '',
    stockLocations: [],
    ...over,
  };
}

function makeContents(over: Partial<PlaceContentsResponse>): PlaceContentsResponse {
  return {
    path: [],
    containerItem: null,
    scopedLowStockCount: 0,
    scopedLentCount: 0,
    childAreas: [],
    containers: [],
    items: [],
    consumables: [],
    ...over,
  };
}

const OVERVIEW: GetInventoryOverviewResponse = {
  expiringWarrantyCount: 0,
  expiringWarranties: [],
  lowStockCount: 0,
  lowStockConsumables: [],
  lentCount: 0,
  lentDurables: [],
  unplacedCount: 2,
  areas: [
    { id: 'garage', name: 'Garage', itemCount: 12, lowStockCount: 3 },
    { id: 'office', name: 'Office', itemCount: 0, lowStockCount: 0 },
  ],
  recentlyUpdated: [],
};

const BIN_CONTENTS = makeContents({
  path: [
    { id: 'garage', name: 'Garage', kind: 'Area' },
    { id: 'chest', name: 'Tool chest', kind: 'Container' },
    { id: 'bin', name: 'Bin', kind: 'Container' },
  ],
  containerItem: makeItem({ id: 'bin', name: 'Bin', isContainer: true }),
  containers: [
    { id: 'tray', name: 'Tray', status: 'Active', itemCount: 2, lowStockCount: 1, previewNames: ['Bits', 'Glue'] },
  ],
  items: [makeItem({ id: 'wrench', name: 'Wrench' })],
  consumables: [
    { consumable: makeConsumable({ id: 'screws', name: 'Wood screws', totalOnHand: 10, isLow: true }), localQuantity: 4 },
  ],
});

describe('PlaceBrowserComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  async function create(
    place: PlaceRef,
    contents: PlaceContentsResponse | null = null,
    overview: GetInventoryOverviewResponse = OVERVIEW,
  ): Promise<ComponentFixture<PlaceBrowserComponent>> {
    const fixture = TestBed.createComponent(PlaceBrowserComponent);
    fixture.componentRef.setInput('overview', overview);
    fixture.componentRef.setInput('place', place);
    fixture.componentRef.setInput('contents', contents);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function el(fixture: ComponentFixture<PlaceBrowserComponent>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function hrefs(fixture: ComponentFixture<PlaceBrowserComponent>): string[] {
    return Array.from(el(fixture).querySelectorAll<HTMLAnchorElement>('a[href]'))
      .map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  it('renders the root grid with per-area drill links and low-stock badges', async () => {
    const fixture = await create(null);

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Browse by place');
    expect(content).toContain('Garage');
    expect(content).toContain('Office');
    expect(content).toContain('Unplaced'); // unplacedCount > 0

    const links = hrefs(fixture);
    expect(links).toContain('/inventory/browse/area/garage');
    expect(links).toContain('/inventory/browse/area/office');
    expect(links).toContain('/inventory/browse/unplaced');

    // One badge for Garage (3 low), none for Office (0).
    const badges = el(fixture).querySelectorAll('[aria-label$="low stock"]');
    expect(badges.length).toBe(1);
    expect(badges[0].getAttribute('aria-label')).toBe('3 low stock');
  });

  it('hides the Unplaced card at zero and shows the cold-start CTAs when empty', async () => {
    let fixture = await create(null, null, { ...OVERVIEW, unplacedCount: 0 });
    expect(hrefs(fixture)).not.toContain('/inventory/browse/unplaced');

    // Cold start (no areas, no unplaced durables): an invitation to add an item, not a dead-end.
    fixture = await create(null, null, { ...OVERVIEW, areas: [], unplacedCount: 0 });
    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Start your inventory');
    expect(content).not.toContain('No areas yet');

    // Primary CTA: a create-entry dialog trigger to add the first item.
    const addButton = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.includes('Add your first item'));
    expect(addButton).toBeTruthy();

    // Secondary CTA: areas reframed as optional, not the gate.
    expect(hrefs(fixture)).toContain('/inventory/areas');
  });

  it('renders exactly one root New-item button at the populated root', async () => {
    const fixture = await create(null);

    const newItemButtons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .filter(b => b.textContent?.trim() === 'New item');
    expect(newItemButtons.length).toBe(1);

    // The button coexists with the grid; full grid/drill-link coverage lives in the root-grid test above.
    expect(hrefs(fixture)).toContain('/inventory/browse/area/garage');
  });

  it('renders child-container cards with subtree counts, previews, and drill links', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Tray');
    expect(content).toContain('Bits, Glue');
    // A child-container card continues spatial drilling within Browse (Explore), not out to detail.
    expect(hrefs(fixture)).toContain('/inventory/browse/container/tray');
    const badges = el(fixture).querySelectorAll('[aria-label$="low stock"]');
    expect(badges.length).toBe(1);
  });

  it('labels each contents group, and names the loose list generically', async () => {
    // BIN_CONTENTS has containers + one durable + one consumable (no child areas).
    let fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    let headings = Array.from(el(fixture).querySelectorAll('h3')).map(h => h.textContent?.trim());
    expect(headings).toContain('Containers');
    expect(headings).toContain('Items');
    expect(headings).not.toContain('Areas');

    // Loose list keeps the generic label regardless of which kinds it holds.
    fixture = await create(
      { kind: 'container', id: 'bin' },
      makeContents({ containers: [], items: [makeItem({ id: 'wrench', name: 'Wrench' })], consumables: [] }),
    );
    headings = Array.from(el(fixture).querySelectorAll('h3')).map(h => h.textContent?.trim());
    expect(headings).toContain('Items');
  });

  /** A loose-contents table row by the visible name (durables + consumables render as table rows now). */
  function row(fixture: ComponentFixture<PlaceBrowserComponent>, name: string): HTMLTableRowElement {
    return Array.from(el(fixture).querySelectorAll<HTMLTableRowElement>('app-inventory-items-view tbody tr'))
      .find(tr => tr.textContent?.includes(name))!;
  }

  it('emits openDurable / openConsumable from the table rows and containerDetails from the header', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    const component = fixture.componentInstance;
    const openDurable = vi.fn();
    const openConsumable = vi.fn();
    const details = vi.fn();
    component.openDurable.subscribe(openDurable);
    component.openConsumable.subscribe(openConsumable);
    component.containerDetails.subscribe(details);

    row(fixture, 'Wrench').click();
    expect(openDurable).toHaveBeenCalledWith(expect.objectContaining({ id: 'wrench' }));

    row(fixture, 'Wood screws').click();
    // Emits the inner ConsumableResponse, not the PlaceConsumableResponse wrapper.
    expect(openConsumable).toHaveBeenCalledWith(expect.objectContaining({ id: 'screws', unit: 'pcs' }));

    Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'View details')!.click();
    expect(details).toHaveBeenCalledWith(expect.objectContaining({ id: 'bin' }));
  });

  it('renders the loose contents as the scoped items-view: no place filter, Location hidden, Show inactive off', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    const view = fixture.debugElement.query(By.directive(InventoryItemsViewComponent))
      .componentInstance as InventoryItemsViewComponent;

    // The place is the route, not a filter; the drilled place has only live statuses; every loose
    // item resolves to this same place, so the Location column is redundant.
    expect(view.showInactiveFilter()).toBe(false);
    // The drilled list inherits the item view-mode pref but shows no toggle of its own.
    expect(view.showViewToggle()).toBe(false);
    expect(view.columnVisibility()).toEqual({ location: false });
    expect(view.rows().map(r => r.id).sort()).toEqual(['screws', 'wrench']);

    const headerText = el(fixture).querySelector('app-inventory-items-view thead')?.textContent ?? '';
    expect(headerText).not.toContain('Location');
  });

  it('shows local consumable quantity with an of-total hint and a low badge', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);

    const onHand = row(fixture, 'Wood screws').querySelector('app-inventory-onhand-cell')!.textContent ?? '';
    expect(onHand).toContain('4');
    expect(onHand).toContain('pcs');
    expect(onHand).toContain('of 10 total');
    expect(onHand).toContain('Low');
  });

  it('shows the empty state with a New-item CTA for an empty place', async () => {
    const fixture = await create(
      { kind: 'container', id: 'bin' },
      makeContents({ ...BIN_CONTENTS, containers: [], items: [], consumables: [] }),
    );

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Nothing here yet');
    const addButtons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .filter(b => b.textContent?.includes('New item'));
    // Only the empty-state CTA — the header no longer duplicates it.
    expect(addButtons.length).toBe(1);
    // The lone CTA is the centered empty-state one, not a table toolbar button.
    expect(el(fixture).querySelector('app-inventory-items-view')).toBeNull();
  });

  it('renders the Add CTA in the items-view toolbar (not the header) when the place has loose items', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);

    const addButtons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .filter(b => b.textContent?.includes('New item'));
    expect(addButtons.length).toBe(1);

    // The single Add button is projected into the items-view toolbar, not the place header.
    const view = el(fixture).querySelector('app-inventory-items-view')!;
    expect(view.contains(addButtons[0])).toBe(true);

    // The drilled container still shows its View-details button in the header.
    const detailsBtn = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'View details');
    expect(detailsBtn).toBeTruthy();
  });

  it('keeps the Add CTA in the header when the place holds only sub-places/containers', async () => {
    const fixture = await create(
      { kind: 'container', id: 'bin' },
      makeContents({
        containerItem: makeItem({ id: 'bin', name: 'Bin', isContainer: true }),
        containers: [
          { id: 'tray', name: 'Tray', status: 'Active', itemCount: 2, lowStockCount: 0, previewNames: [] },
        ],
        items: [],
        consumables: [],
      }),
    );

    const addButtons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .filter(b => b.textContent?.includes('New item'));
    expect(addButtons.length).toBe(1);

    // No loose items means no table — the Add button stays in the header.
    expect(el(fixture).querySelector('app-inventory-items-view')).toBeNull();
    const headings = Array.from(el(fixture).querySelectorAll('h3')).map(h => h.textContent?.trim());
    expect(headings).not.toContain('Items');
  });

  it('shows the View-details button only when a container is drilled', async () => {
    let fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    let detailsBtn = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'View details');
    expect(detailsBtn).toBeTruthy();

    fixture = await create(
      { kind: 'area', id: 'garage' },
      makeContents({ path: [{ id: 'garage', name: 'Garage', kind: 'Area' }] }),
    );
    detailsBtn = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'View details');
    expect(detailsBtn).toBeUndefined();
  });

  it('labels an Unplaced drill from its place ref', async () => {
    const fixture = await create({ kind: 'unplaced' }, makeContents({}));

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Unplaced');
  });
});
