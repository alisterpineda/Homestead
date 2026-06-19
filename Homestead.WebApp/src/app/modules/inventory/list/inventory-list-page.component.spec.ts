import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { InventoryListPageComponent } from './inventory-list-page.component';
import { InventoryItemsViewComponent } from './inventory-items-view.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import type { InventoryRow } from './inventory-row';
import { durableToRow, consumableToRow } from './inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
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
const TAPE = makeConsumable({
  id: 'tape', name: 'Tape', totalOnHand: 5,
  stockLocations: [{
    id: 'e-tape', quantity: 5, areaId: GARAGE,
    path: [{ id: GARAGE, name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
  }],
});
const SCREWS = makeConsumable({
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
const GLUE = makeConsumable({ id: 'glue', name: 'Glue', totalOnHand: 0 });
const LOOSE = makeConsumable({
  id: 'loose', name: 'Loose washers', totalOnHand: 3,
  stockLocations: [{
    id: 'e-loose', quantity: 3, containerItemId: 'floatbin',
    path: [{ id: 'floatbin', name: 'Float bin', kind: 'Container' }], locationLabel: 'Float bin',
  }],
});

class StubDurableApi {
  // Honors the server-side filters the page uses: ParentItemId (drill-down) and Search.
  getDurables = (p: { ParentItemId?: string; Search?: string }) => {
    let items = p?.ParentItemId ? ITEMS.filter(i => i.parentItemId === p.ParentItemId) : ITEMS;
    if (p?.Search) items = items.filter(i => i.name.toLowerCase().includes(p.Search!.toLowerCase()));
    return of({ items, page: 1, pageSize: 200, totalCount: items.length });
  };
  getDurable = (p: { id: string }) => {
    const found = ITEMS.find(i => i.id === p.id);
    return found ? of(found) : throwError(() => new Error('404'));
  };
  patchDurable = () => of(makeItem({}));
  deleteDurable = () => of(undefined);
}
class StubConsumableApi {
  getConsumables = (p?: { Search?: string }) => {
    const items = p?.Search
      ? CONSUMABLES.filter(s => s.name.toLowerCase().includes(p.Search!.toLowerCase()))
      : CONSUMABLES;
    return of({ items, page: 1, pageSize: 200, totalCount: items.length });
  };
  patchConsumable = () => of(makeConsumable({}));
  deleteConsumable = () => of(undefined);
}
class StubAreaApi {
  getAreas = () => of({ items: AREAS, page: 1, pageSize: 200, totalCount: AREAS.length });
}

const ITEMS: DurableResponse[] = [CHEST, WRENCH, BIN, SCREW, ARCHIVED, FLOATING, LADDER];
const CONSUMABLES: ConsumableResponse[] = [TAPE, SCREWS, GLUE, LOOSE];
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
        { provide: InventoryDurableApiService, useValue: new StubDurableApi() },
        { provide: InventoryConsumableApiService, useValue: new StubConsumableApi() },
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

  /** The hosted items-view child — the page passes it the place-scoped rows + deep-link seeds. */
  function itemsView(f: ComponentFixture<InventoryListPageComponent> = fixture): InventoryItemsViewComponent {
    return f.debugElement.query(By.directive(InventoryItemsViewComponent)).componentInstance;
  }

  // Let the drill-down's server fetch (getDurable + getDurables(ParentItemId)) resolve.
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('loads both items and consumables into one row set', () => {
    expect(component.allDurables().length).toBe(ITEMS.length);
    expect(component.allConsumables().length).toBe(CONSUMABLES.length);
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);
  });

  it('resolved-area filter matches nested items and consumables stocked there', () => {
    component.areaFilter.set(GARAGE);
    // Items that walk up to Garage plus consumables with stock resolving to Garage —
    // including screws, whose second entry sits inside the chest container.
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'chest', 'ladder', 'screw', 'screws', 'tape', 'wrench']);
    // The zero-stock consumable only appears under "All areas".
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('resolved-unplaced bucket matches unplaced items and stock with no area root', () => {
    component.areaFilter.set('unplaced');
    expect(ids(scopedRows())).toEqual(['floating', 'loose']);
    // Zero stock is not "unplaced stock" — glue matches neither bucket.
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('drill-down scopes to direct item children plus consumables stocked in the container', async () => {
    component.onViewContents(durableToRow(CHEST));
    await settle();
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'screws', 'wrench']);
    // The grandchild (screw, under bin) is never in chest's direct-children scope.
    expect(scopedRows().some(r => r.id === 'screw')).toBe(false);
    // The items-view hides the archived child by default (show-inactive off).
    expect(ids(itemsView().visibleRows())).toEqual(['bin', 'screws', 'wrench']);
  });

  it('drilled consumable rows show the local quantity with an of-total hint', async () => {
    component.containerFilter.set('chest');
    await settle();
    const screws = scopedRows().find(r => r.id === 'screws')!;
    expect(screws.onHand).toBe(4);
    expect(screws.onHandHint).toBe('of 10 total');
    expect(screws.locationText).toBe('Garage › Tool chest');
  });

  it('clears container scope when an area filter is picked or the scope is reset', () => {
    component.containerFilter.set('chest');
    component.selectAreaFilter(GARAGE);
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe(GARAGE);

    component.containerFilter.set('chest');
    component.onReset();
    expect(component.containerFilter()).toBeNull();
    expect(component.areaFilter()).toBe('all');
  });

  it('builds a breadcrumb from the container path plus its own name', async () => {
    component.onViewContents(durableToRow(BIN));
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

  it('exposes the container/area scope as the items-view trigger label', () => {
    expect(component['scopeLabel']()).toBeNull();
    component.areaFilter.set(GARAGE);
    expect(component['scopeLabel']()).toBe('Filter: Garage');
    component.areaFilter.set('unplaced');
    expect(component['scopeLabel']()).toBe('Filter: Unplaced');
    component.containerFilter.set('chest');
    expect(component['scopeLabel']()).toContain('In:');
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
    expect(ids(component.allDurables().map(durableToRow))).toEqual(['screw']);
    expect(ids(component.allConsumables().map(consumableToRow))).toEqual(['screws']);
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

    component.onViewContents(durableToRow(CHEST));
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
    component.onViewContents(durableToRow(CHEST)); // clears search, enters drill
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
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);
  });

  it('never commits whitespace-only or untrimmed terms to the URL', async () => {
    component.searchInput.set('   ');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);

    component.searchInput.set('screw ');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
  });

  it('onReset clears the committed search along with the scope', async () => {
    await navigateSearch('screw');
    component.areaFilter.set(GARAGE);

    component.onReset();
    await settle();
    expect(component.searchInput()).toBe('');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(component.areaFilter()).toBe('all');
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);
  });

  // Deep links from the overview tiles: params seed the items-view (kind/low/lent) and the page's own
  // scope (area/container) once at construction, and are then stripped from the URL.
  describe('deep-link param seeding', () => {
    async function createWithParams(params: Record<string, string>): Promise<ComponentFixture<InventoryListPageComponent>> {
      await TestBed.inject(Router).navigate([], { queryParams: params });
      const f = TestBed.createComponent(InventoryListPageComponent);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();
      return f;
    }

    it('?lowStock=true seeds the items-view low-stock lens and strips the param', async () => {
      const f = await createWithParams({ lowStock: 'true' });
      expect(itemsView(f).lowStockOnly()).toBe(true);
      expect(ids(itemsView(f).visibleRows())).toEqual(['screws']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['lowStock']).toBeUndefined();
    });

    it('?lent=true seeds the lent lens and shows only Lent rows', async () => {
      const f = await createWithParams({ lent: 'true' });
      expect(itemsView(f).lentOnly()).toBe(true);
      expect(ids(itemsView(f).visibleRows())).toEqual(['ladder']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['lent']).toBeUndefined();
    });

    it('?area and ?kind seed the page area scope and the items-view kind together', async () => {
      const f = await createWithParams({ area: 'unplaced', kind: 'durables' });
      expect(f.componentInstance.areaFilter()).toBe('unplaced');
      expect(itemsView(f).kind()).toBe('durables');
      expect(ids(itemsView(f).visibleRows())).toEqual(['floating']);
      const stripped = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(stripped['area']).toBeUndefined();
      expect(stripped['kind']).toBeUndefined();
    });

    it('an unknown ?kind value falls back to all kinds', async () => {
      const f = await createWithParams({ kind: 'bogus' });
      expect(itemsView(f).kind()).toBe('all');
    });

    it('?container seeds the drill-down scope and strips the param', async () => {
      const f = await createWithParams({ container: 'chest' });
      expect(f.componentInstance.containerFilter()).toBe('chest');
      expect(ids(itemsView(f).visibleRows())).toEqual(['bin', 'screws', 'wrench']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['container']).toBeUndefined();
    });

    it('param stripping preserves an accompanying ?search', async () => {
      const f = await createWithParams({ lowStock: 'true', search: 'screw' });
      expect(itemsView(f).lowStockOnly()).toBe(true);
      const params = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(params['lowStock']).toBeUndefined();
      expect(params['search']).toBe('screw');
    });
  });

  it('opens the matching quick panel when the items-view dispatches a row open', () => {
    component.onOpenDurable(CHEST);
    expect(component.quickPanelDurable()).toBe(CHEST);
    expect(component.durableQuickPanelState()).toBe('open');
    expect(component.consumableQuickPanelState()).toBe('closed');

    component.onOpenConsumable(TAPE);
    expect(component.quickPanelConsumable()).toBe(TAPE);
    expect(component.consumableQuickPanelState()).toBe('open');
  });
});
