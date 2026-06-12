import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { InventoryListPageComponent } from './inventory-list-page.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import type { InventoryRow } from './inventory-row';
import { itemToRow, supplyToRow } from './inventory-row';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { ItemStatus } from '@/api/generated/models/item-status';

// brn-select (in child sheets) uses ResizeObserver; safe to stub for the rendered template.
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

const GARAGE = 'garage';

// chest(garage) ← wrench, bin(garage), archivedInChest ; bin ← screw(grandchild) ; floating(unplaced)
const CHEST = makeItem({ id: 'chest', name: 'Tool chest', isContainer: true, areaId: GARAGE, resolvedAreaId: GARAGE });
const WRENCH = makeItem({ id: 'wrench', name: 'Wrench', parentItemId: 'chest', resolvedAreaId: GARAGE });
const BIN = makeItem({
  id: 'bin', name: 'Bin', isContainer: true, parentItemId: 'chest', resolvedAreaId: GARAGE,
  path: [
    { id: GARAGE, name: 'Garage', kind: 'Area' },
    { id: 'chest', name: 'Tool chest', kind: 'Container' },
  ],
});
const SCREW = makeItem({ id: 'screw', name: 'Screw', parentItemId: 'bin', resolvedAreaId: GARAGE });
const ARCHIVED = makeItem({ id: 'archived', name: 'Old rag', status: 'Archived' as ItemStatus, parentItemId: 'chest', resolvedAreaId: GARAGE });
const FLOATING = makeItem({ id: 'floating', name: 'Floating thing' });
const LADDER = makeItem({ id: 'ladder', name: 'Ladder', status: 'Lent' as ItemStatus, areaId: GARAGE, resolvedAreaId: GARAGE, lentTo: 'Dave' });

// tape: stocked in the garage area ; screws: split chest(4) + garage(6), low ;
// glue: zero stock ; loose: stocked in an unplaced container (no area root)
const TAPE = makeSupply({
  id: 'tape', name: 'Tape', totalOnHand: 5,
  stockLocations: [{
    id: 'e-tape', quantity: 5, areaId: GARAGE,
    path: [{ id: GARAGE, name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
  }],
});
const SCREWS = makeSupply({
  id: 'screws', name: 'Wood screws', totalOnHand: 10, isLow: true,
  stockLocations: [
    {
      id: 'e-screws-garage', quantity: 6, areaId: GARAGE,
      path: [{ id: GARAGE, name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
    },
    {
      id: 'e-screws-chest', quantity: 4, containerItemId: 'chest',
      path: [
        { id: GARAGE, name: 'Garage', kind: 'Area' },
        { id: 'chest', name: 'Tool chest', kind: 'Container' },
      ],
      locationLabel: 'Garage › Tool chest',
    },
  ],
});
const GLUE = makeSupply({ id: 'glue', name: 'Glue', totalOnHand: 0 });
const LOOSE = makeSupply({
  id: 'loose', name: 'Loose washers', totalOnHand: 3,
  stockLocations: [{
    id: 'e-loose', quantity: 3, containerItemId: 'floatbin',
    path: [{ id: 'floatbin', name: 'Float bin', kind: 'Container' }], locationLabel: 'Float bin',
  }],
});

class StubItemApi {
  // Honors the server-side filters the page uses: ParentItemId (drill-down) and Search.
  getItems = (p: { ParentItemId?: string; Search?: string }) => {
    let items = p?.ParentItemId ? ITEMS.filter(i => i.parentItemId === p.ParentItemId) : ITEMS;
    if (p?.Search) items = items.filter(i => i.name.toLowerCase().includes(p.Search!.toLowerCase()));
    return of({ items, page: 1, pageSize: 200, totalCount: items.length });
  };
  getItem = (p: { id: string }) => {
    const found = ITEMS.find(i => i.id === p.id);
    return found ? of(found) : throwError(() => new Error('404'));
  };
  patchItem = () => of(makeItem({}));
  deleteItem = () => of(undefined);
}
class StubSupplyApi {
  getSupplies = (p?: { Search?: string }) => {
    const items = p?.Search
      ? SUPPLIES.filter(s => s.name.toLowerCase().includes(p.Search!.toLowerCase()))
      : SUPPLIES;
    return of({ items, page: 1, pageSize: 200, totalCount: items.length });
  };
  patchSupply = () => of(makeSupply({}));
  deleteSupply = () => of(undefined);
}
class StubAreaApi {
  getAreas = () => of({ items: AREAS, page: 1, pageSize: 200, totalCount: AREAS.length });
}

const ITEMS: ItemResponse[] = [CHEST, WRENCH, BIN, SCREW, ARCHIVED, FLOATING, LADDER];
const SUPPLIES: SupplyResponse[] = [TAPE, SCREWS, GLUE, LOOSE];
const AREAS: AreaResponse[] = [{ id: GARAGE, name: 'Garage', liveItemCount: 0, createdAt: '', updatedAt: '' }];

describe('InventoryListPageComponent', () => {
  let fixture: ComponentFixture<InventoryListPageComponent>;
  let component: InventoryListPageComponent;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryItemApiService, useValue: new StubItemApi() },
        { provide: InventorySupplyApiService, useValue: new StubSupplyApi() },
        { provide: InventoryAreaApiService, useValue: new StubAreaApi() },
      ],
    });
    fixture = TestBed.createComponent(InventoryListPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function ids(rows: readonly InventoryRow[]): string[] {
    return rows.map(r => r.id).sort();
  }

  function scopedRows(): InventoryRow[] {
    return component['scopedRows']();
  }

  // Let the drill-down's server fetch (getItem + getItems(ParentItemId)) resolve.
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('loads both items and supplies into one row set', () => {
    expect(component.allItems().length).toBe(ITEMS.length);
    expect(component.allSupplies().length).toBe(SUPPLIES.length);
    expect(scopedRows().length).toBe(ITEMS.length + SUPPLIES.length);
  });

  it('kind filter narrows to items or supplies', () => {
    component.kindFilter.set('items');
    expect(ids(component['kindRows']())).toEqual(['archived', 'bin', 'chest', 'floating', 'ladder', 'screw', 'wrench']);

    component.kindFilter.set('supplies');
    expect(ids(component['kindRows']())).toEqual(['glue', 'loose', 'screws', 'tape']);

    component.kindFilter.set('all');
    expect(component['kindRows']().length).toBe(ITEMS.length + SUPPLIES.length);
  });

  it('resolved-area filter matches nested items and supplies stocked there', () => {
    component.areaFilter.set(GARAGE);
    // Items that walk up to Garage plus supplies with stock resolving to Garage —
    // including screws, whose second entry sits inside the chest container.
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'chest', 'ladder', 'screw', 'screws', 'tape', 'wrench']);
    // The zero-stock supply only appears under "All areas".
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('resolved-unplaced bucket matches unplaced items and stock with no area root', () => {
    component.areaFilter.set('unplaced');
    expect(ids(scopedRows())).toEqual(['floating', 'loose']);
    // Zero stock is not "unplaced stock" — glue matches neither bucket.
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('drill-down scopes to direct item children plus supplies stocked in the container', async () => {
    component.onViewContents(itemToRow(CHEST));
    await settle();
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'screws', 'wrench']);
    // Show-inactive off by default hides the archived direct child.
    expect(ids(component.visibleRows())).toEqual(['bin', 'screws', 'wrench']);
    // The grandchild (screw, under bin) is never in chest's direct-children scope.
    expect(scopedRows().some(r => r.id === 'screw')).toBe(false);
  });

  it('drilled supply rows show the local quantity with an of-total hint', async () => {
    component.containerFilter.set('chest');
    await settle();
    const screws = scopedRows().find(r => r.id === 'screws')!;
    expect(screws.onHand).toBe(4);
    expect(screws.onHandHint).toBe('of 10 total');
    expect(screws.locationText).toBe('Garage › Tool chest');
  });

  it('low-stock filter keeps only low supplies (the reorder view)', () => {
    component.lowStockOnly.set(true);
    expect(ids(component.visibleRows())).toEqual(['screws']);
    expect(component.lowCount()).toBe(1);
  });

  it('counts inactive direct children inside a drilled container', async () => {
    component.containerFilter.set('chest');
    await settle();
    expect(component.inactiveCount()).toBe(1);
  });

  it('clears container scope when an area filter is picked or filters are reset', () => {
    component.containerFilter.set('chest');
    component.selectAreaFilter(GARAGE);
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe(GARAGE);

    component.containerFilter.set('chest');
    component.lowStockOnly.set(true);
    component.resetFilters();
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe('all');
    expect(component.lowStockOnly()).toBe(false);
  });

  it('builds a breadcrumb from the container path plus its own name', async () => {
    component.onViewContents(itemToRow(BIN));
    await settle();
    const crumb = component.breadcrumb();
    expect(crumb.map(s => s.name)).toEqual(['Garage', 'Tool chest', 'Bin']);
    expect(crumb[crumb.length - 1].id).toBe('bin');
  });

  it('keeps the breadcrumb exit reachable when the drilled container metadata is missing', async () => {
    // Container fetch fails / returns nothing → containerItem is null, but the scope (and its exit)
    // must remain: the breadcrumb block is gated on containerFilter, not on the loaded metadata.
    component.containerFilter.set('does-not-exist');
    await settle();
    expect(component.containerItem()).toBeNull();
    expect(component.containerFilter()).toBe('does-not-exist');
    component.clearContainerFilter();
    expect(component.containerFilter()).toBeNull();
  });

  it('shows the rejection reason inline and keeps the dialog open when a delete is blocked', () => {
    const supplyApi = TestBed.inject(InventorySupplyApiService) as unknown as StubSupplyApi;
    supplyApi.deleteSupply = () => throwError(() => new HttpErrorResponse({
      status: 400,
      error: { detail: 'This supply has stock history — archive it instead.' },
    }));

    component.onDeleteRequest(supplyToRow(SCREWS));
    expect(component.deleteError()).toBe('');
    let closed = false;
    component.confirmDelete({ close: () => { closed = true; } });

    expect(closed).toBe(false);
    expect(component.deleteError()).toBe('This supply has stock history — archive it instead.');
    expect(component.pendingDelete()?.id).toBe('screws');
    expect(component.deleting()).toBe(false);

    // Re-requesting a delete starts with a clean slate.
    component.onDeleteRequest(supplyToRow(TAPE));
    expect(component.deleteError()).toBe('');
  });

  // Drive the committed term the way the app does — through the URL.
  async function navigateSearch(term: string | null): Promise<void> {
    await TestBed.inject(Router).navigate([], { queryParams: { search: term } });
    await settle();
  }

  // Let the 300ms searchInput debounce fire and the resulting navigation/fetch settle.
  async function flushDebounce(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, 350));
    await settle();
  }

  it('passes the committed search term to both server fetches', async () => {
    await navigateSearch('screw');
    expect(ids(component.allItems().map(itemToRow))).toEqual(['screw']);
    expect(ids(component.allSupplies().map(supplyToRow))).toEqual(['screws']);
    expect(ids(scopedRows())).toEqual(['screw', 'screws']);
  });

  it('keeps the toolbar with "Nothing matches" on a zero-result search', async () => {
    await navigateSearch('zzz-no-such-thing');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Nothing matches');
    expect(text).not.toContain('No items yet');
    // The toolbar (and with it the search box) must stay reachable to clear the search.
    expect((fixture.nativeElement as HTMLElement).querySelector('input[hlmInput]')).not.toBeNull();
  });

  it('committing a typed search drops drill-down and area scope and syncs the URL', async () => {
    component.containerFilter.set('chest');
    component.areaFilter.set(GARAGE);
    component.searchInput.set('screw');
    await flushDebounce();

    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe('all');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
    expect(ids(scopedRows())).toEqual(['screw', 'screws']);
  });

  it('entering a drill-down clears an active search', async () => {
    await navigateSearch('screw');
    component.searchInput.set('screw');

    component.onViewContents(itemToRow(CHEST));
    await settle();
    expect(component.searchInput()).toBe('');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'screws', 'wrench']);

    // The debounced '' emission must not re-trigger anything after the immediate clear.
    await flushDebounce();
    expect(component.containerFilter()).toBe('chest');
  });

  it('re-committing the same term immediately after a programmatic clear still searches', async () => {
    await navigateSearch('screw');
    component.onViewContents(itemToRow(CHEST)); // clears search, enters drill
    await settle();
    expect(component.searchInput()).toBe('');

    // Re-typed before the clear's own debounce window closes — must not be swallowed.
    component.searchInput.set('screw');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
    expect(component.containerFilter()).toBeNull();
    expect(ids(scopedRows())).toEqual(['screw', 'screws']);
  });

  it('keeps the search box in step with externally-driven URL changes', async () => {
    await navigateSearch('screw');
    expect(component.searchInput()).toBe('screw');

    // e.g. a sidebar link or history traversal removing the param while the component is reused.
    await navigateSearch(null);
    expect(component.searchInput()).toBe('');
    expect(scopedRows().length).toBe(ITEMS.length + SUPPLIES.length);
  });

  it('never commits whitespace-only or untrimmed terms to the URL', async () => {
    component.searchInput.set('   ');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(scopedRows().length).toBe(ITEMS.length + SUPPLIES.length);

    component.searchInput.set('screw ');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
  });

  it('resetFilters clears the committed search along with the other filters', async () => {
    await navigateSearch('screw');
    component.lowStockOnly.set(true);

    component.resetFilters();
    await settle();
    expect(component.searchInput()).toBe('');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(component.lowStockOnly()).toBe(false);
    expect(scopedRows().length).toBe(ITEMS.length + SUPPLIES.length);
  });

  it('lent-only filter keeps only Lent rows', () => {
    component.lentOnly.set(true);
    expect(ids(component.visibleRows())).toEqual(['ladder']);
    expect(component.lentCount()).toBe(1);
  });

  it('resetFilters clears the lent-only filter', () => {
    component.lentOnly.set(true);
    component.resetFilters();
    expect(component.lentOnly()).toBe(false);
  });

  // Deep links from the overview tiles: params seed the filter signals once at construction and
  // are then stripped from the URL.
  describe('deep-link param seeding', () => {
    async function createWithParams(params: Record<string, string>): Promise<InventoryListPageComponent> {
      await TestBed.inject(Router).navigate([], { queryParams: params });
      const f = TestBed.createComponent(InventoryListPageComponent);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();
      return f.componentInstance;
    }

    it('?lowStock=true seeds the low-stock filter and strips the param', async () => {
      const c = await createWithParams({ lowStock: 'true' });
      expect(c.lowStockOnly()).toBe(true);
      expect(ids(c.visibleRows())).toEqual(['screws']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['lowStock']).toBeUndefined();
    });

    it('?lent=true seeds the lent filter and shows only Lent rows', async () => {
      const c = await createWithParams({ lent: 'true' });
      expect(c.lentOnly()).toBe(true);
      expect(ids(c.visibleRows())).toEqual(['ladder']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['lent']).toBeUndefined();
    });

    it('?area and ?kind seed their filters together (the unplaced tile link)', async () => {
      const c = await createWithParams({ area: 'unplaced', kind: 'items' });
      expect(c.areaFilter()).toBe('unplaced');
      expect(c.kindFilter()).toBe('items');
      expect(ids(c.visibleRows())).toEqual(['floating']);
      const stripped = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(stripped['area']).toBeUndefined();
      expect(stripped['kind']).toBeUndefined();
    });

    it('an unknown ?kind value falls back to all kinds', async () => {
      const c = await createWithParams({ kind: 'bogus' });
      expect(c.kindFilter()).toBe('all');
    });

    it('param stripping preserves an accompanying ?search', async () => {
      const c = await createWithParams({ lowStock: 'true', search: 'screw' });
      expect(c.lowStockOnly()).toBe(true);
      const params = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(params['lowStock']).toBeUndefined();
      expect(params['search']).toBe('screw');
    });
  });

  it('dispatches open and edit by row kind', () => {
    component.onOpen(itemToRow(CHEST));
    expect(component.quickPanelItem()).toBe(CHEST);
    expect(component.itemQuickPanelState()).toBe('open');
    expect(component.supplyQuickPanelState()).toBe('closed');

    component.onOpen(supplyToRow(TAPE));
    expect(component.quickPanelSupply()).toBe(TAPE);
    expect(component.supplyQuickPanelState()).toBe('open');

    component.onEdit(itemToRow(WRENCH));
    expect(component.selectedItem()).toBe(WRENCH);
    expect(component.itemSheetState()).toBe('open');
    expect(component.supplySheetState()).toBe('closed');

    component.onEdit(supplyToRow(GLUE));
    expect(component.selectedSupply()).toBe(GLUE);
    expect(component.supplySheetState()).toBe('open');
  });
});
