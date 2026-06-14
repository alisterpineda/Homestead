import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { PlaceBrowserComponent } from './place-browser.component';
import type { PlaceRef } from './place-param';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';

// The embedded create dialog hosts pickers that use ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(over: Partial<ItemResponse>): ItemResponse {
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

function makeSupply(over: Partial<SupplyResponse>): SupplyResponse {
  return {
    id: 's',
    name: 'Supply',
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
    directItemCount: 0,
    nestedItemCount: 0,
    scopedLowStockCount: 0,
    scopedLentCount: 0,
    childAreas: [],
    containers: [],
    items: [],
    supplies: [],
    ...over,
  };
}

const OVERVIEW: GetInventoryOverviewResponse = {
  lowStockCount: 0,
  lowStockSupplies: [],
  lentCount: 0,
  lentItems: [],
  unplacedCount: 2,
  unplacedItems: [],
  areas: [
    { id: 'garage', name: 'Garage', itemCount: 12, lowStockCount: 3 },
    { id: 'office', name: 'Office', itemCount: 0, lowStockCount: 0 },
  ],
};

const BIN_CONTENTS = makeContents({
  path: [
    { id: 'garage', name: 'Garage', kind: 'Area' },
    { id: 'chest', name: 'Tool chest', kind: 'Container' },
    { id: 'bin', name: 'Bin', kind: 'Container' },
  ],
  containerItem: makeItem({ id: 'bin', name: 'Bin', isContainer: true }),
  directItemCount: 3,
  nestedItemCount: 2,
  containers: [
    { id: 'tray', name: 'Tray', status: 'Active', itemCount: 2, lowStockCount: 1, previewNames: ['Bits', 'Glue'] },
  ],
  items: [makeItem({ id: 'wrench', name: 'Wrench' })],
  supplies: [
    { supply: makeSupply({ id: 'screws', name: 'Wood screws', totalOnHand: 10, isLow: true }), localQuantity: 4 },
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

  it('hides the Unplaced card at zero and shows the no-areas hint when empty', async () => {
    let fixture = await create(null, null, { ...OVERVIEW, unplacedCount: 0 });
    expect(hrefs(fixture)).not.toContain('/inventory/browse/unplaced');

    fixture = await create(null, null, { ...OVERVIEW, areas: [], unplacedCount: 0 });
    expect(el(fixture).textContent).toContain('No areas yet');
    expect(hrefs(fixture)).toContain('/inventory/areas');
  });

  it('shows the counts line with the nested part hidden at zero', async () => {
    let fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    expect(el(fixture).textContent).toContain('3 here · 2 more inside containers');

    fixture = await create(
      { kind: 'container', id: 'bin' },
      makeContents({ ...BIN_CONTENTS, nestedItemCount: 0 }),
    );
    const content = el(fixture).textContent ?? '';
    expect(content).toContain('3 here');
    expect(content).not.toContain('more inside containers');
  });

  it('renders child-container cards with subtree counts, previews, and drill links', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Tray');
    expect(content).toContain('Bits, Glue');
    // A child-container card drills into that container's detail page, not the browse surface.
    expect(hrefs(fixture)).toContain('/inventory/items/tray');
    const badges = el(fixture).querySelectorAll('[aria-label$="low stock"]');
    expect(badges.length).toBe(1);
  });

  it('emits openItem / openSupply / containerDetails from their controls', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    const component = fixture.componentInstance;
    const openItem = vi.fn();
    const openSupply = vi.fn();
    const details = vi.fn();
    component.openItem.subscribe(openItem);
    component.openSupply.subscribe(openSupply);
    component.containerDetails.subscribe(details);

    const buttons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'));
    buttons.find(b => b.textContent?.includes('Wrench'))!.click();
    expect(openItem).toHaveBeenCalledWith(expect.objectContaining({ id: 'wrench' }));

    buttons.find(b => b.textContent?.includes('Wood screws'))!.click();
    // Emits the inner SupplyResponse, not the PlaceSupplyResponse wrapper.
    expect(openSupply).toHaveBeenCalledWith(expect.objectContaining({ id: 'screws', unit: 'pcs' }));

    buttons.find(b => b.textContent?.trim() === 'Details')!.click();
    expect(details).toHaveBeenCalledWith(expect.objectContaining({ id: 'bin' }));
  });

  it('shows local supply quantity with an of-total hint and a low badge', async () => {
    const fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('4 pcs');
    expect(content).toContain('of 10 total');
    expect(content).toContain('Low');
  });

  it('shows the empty state with an Add-item-here CTA for an empty place', async () => {
    const fixture = await create(
      { kind: 'container', id: 'bin' },
      makeContents({ ...BIN_CONTENTS, containers: [], items: [], supplies: [], directItemCount: 0, nestedItemCount: 0 }),
    );

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Nothing here yet');
    const addButtons = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .filter(b => b.textContent?.includes('Add item here'));
    // Header CTA + empty-state CTA.
    expect(addButtons.length).toBe(2);
  });

  it('targets Open-as-list per scope', async () => {
    let fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    expect(hrefs(fixture)).toContain('/inventory/items?container=bin');

    fixture = await create(
      { kind: 'area', id: 'garage' },
      makeContents({ path: [{ id: 'garage', name: 'Garage', kind: 'Area' }] }),
    );
    expect(hrefs(fixture)).toContain('/inventory/items?area=garage');

    fixture = await create({ kind: 'unplaced' }, makeContents({}));
    expect(hrefs(fixture)).toContain('/inventory/items?area=unplaced&kind=items');
  });

  it('shows the Details button only when a container is drilled', async () => {
    let fixture = await create({ kind: 'container', id: 'bin' }, BIN_CONTENTS);
    let detailsBtn = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'Details');
    expect(detailsBtn).toBeTruthy();

    fixture = await create(
      { kind: 'area', id: 'garage' },
      makeContents({ path: [{ id: 'garage', name: 'Garage', kind: 'Area' }] }),
    );
    detailsBtn = Array.from(el(fixture).querySelectorAll<HTMLButtonElement>('button'))
      .find(b => b.textContent?.trim() === 'Details');
    expect(detailsBtn).toBeUndefined();
  });

  it('labels an Unplaced drill from its place ref', async () => {
    const fixture = await create({ kind: 'unplaced' }, makeContents({ directItemCount: 1 }));

    const content = el(fixture).textContent ?? '';
    expect(content).toContain('Unplaced');
    expect(content).toContain('1 here');
  });
});
