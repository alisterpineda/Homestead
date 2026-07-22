import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { InventoryListPageComponent } from './inventory-list-page.component';
import { InventoryItemsViewComponent } from './inventory-items-view.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryTagApiService } from '@/api/services/inventory-tag-api.service';
import type { InventoryRow } from './inventory-row';
import { durableToRow, consumableToRow } from './inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { GetInventoryTagsResponse } from '@/api/generated/models/get-inventory-tags-response';
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
const APPLE = 'apple';
const WRENCH = makeItem({ id: 'wrench', name: 'Wrench', parentItemId: 'chest', resolvedAreaId: GARAGE, brandId: APPLE, brandName: 'Apple' });
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
  id: 'tape', name: 'Tape', totalOnHand: 5, brandId: APPLE, brandName: 'Apple',
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

// The list page now filters by RESOLVED area on the server, so the stubs simulate that filter to
// prove the page passes the fetch params through (no client-side area pass). Test AREAS has no
// nesting, so an area's inclusive subtree is just itself.
interface ListFetchParams { Search?: string; AreaId?: string; Unplaced?: boolean; BrandId?: string; TagIds?: string[]; IncludeChildTags?: boolean; totalCountOverride?: number }

function durableResolvedArea(d: DurableResponse): string | null {
  return d.resolvedAreaId ?? null;
}
function consumableResolvedAreas(c: ConsumableResponse): (string | null)[] {
  return (c.stockLocations ?? []).map(loc => {
    let resolved: string | null = null;
    for (const seg of loc.path ?? []) if (seg.kind === 'Area') resolved = seg.id;
    return resolved;
  });
}

class StubDurableApi {
  lastParams: ListFetchParams | undefined;
  totalCountOverride: number | null = null;
  getDurables = (p: ListFetchParams) => {
    this.lastParams = p;
    let items = ITEMS;
    if (p?.Search) items = items.filter(i => i.name.toLowerCase().includes(p.Search!.toLowerCase()));
    if (p?.AreaId) items = items.filter(i => durableResolvedArea(i) === p.AreaId);
    else if (p?.Unplaced) items = items.filter(i => durableResolvedArea(i) === null);
    if (p?.BrandId) items = items.filter(i => i.brandId === p.BrandId);
    if (p?.TagIds) items = items.filter(i => (i.tags ?? []).some(t => p.TagIds!.includes(t.id)));
    return of({ items, page: 1, pageSize: 200, totalCount: this.totalCountOverride ?? items.length });
  };
  patchDurable = () => of(makeItem({}));
  deleteDurable = () => of(undefined);
}
class StubConsumableApi {
  lastParams: ListFetchParams | undefined;
  totalCountOverride: number | null = null;
  getConsumables = (p: ListFetchParams) => {
    this.lastParams = p;
    let items = CONSUMABLES;
    if (p?.Search) items = items.filter(s => s.name.toLowerCase().includes(p.Search!.toLowerCase()));
    if (p?.AreaId) items = items.filter(s => consumableResolvedAreas(s).includes(p.AreaId!));
    else if (p?.Unplaced) items = items.filter(s => consumableResolvedAreas(s).includes(null));
    if (p?.BrandId) items = items.filter(s => s.brandId === p.BrandId);
    if (p?.TagIds) items = items.filter(s => (s.tags ?? []).some(t => p.TagIds!.includes(t.id)));
    return of({ items, page: 1, pageSize: 200, totalCount: this.totalCountOverride ?? items.length });
  };
  patchConsumable = () => of(makeConsumable({}));
  deleteConsumable = () => of(undefined);
}
class StubAreaApi {
  getAreas = () => of({ items: AREAS, page: 1, pageSize: 200, totalCount: AREAS.length });
}
class StubBrandApi {
  getBrands = () => of({ items: BRANDS, page: 1, pageSize: 200, totalCount: BRANDS.length });
}
// The refine popover's tag checklist (getTags) and the selected-chip resolver (getTagsByIds). The
// list fixtures carry no tags, so any TagIds fetch filters to empty — exactly the empty-state case.
class StubTagApi {
  getTags = () => of({ items: TAGS, page: 1, pageSize: 50, totalCount: TAGS.length });
  getTagsByIds = (params: { body: { ids: string[] } }) =>
    of(params.body.ids.map(id => ({ id, path: `tag/${id}` })));
}

const ITEMS: DurableResponse[] = [CHEST, WRENCH, BIN, SCREW, ARCHIVED, FLOATING, LADDER];
const CONSUMABLES: ConsumableResponse[] = [TAPE, SCREWS, GLUE, LOOSE];
const AREAS: AreaResponse[] = [{ id: GARAGE, name: 'Garage', liveItemCount: 0, createdAt: '', updatedAt: '' }];
const BRANDS = [{ id: APPLE, name: 'Apple' }, { id: 'sony', name: 'Sony' }];
const TAGS: GetInventoryTagsResponse[] = [
  { id: 't-a', name: 'A', path: 'A', itemCount: 0, createdAt: '', updatedAt: '' },
  { id: 't-b', name: 'B', path: 'B', itemCount: 0, createdAt: '', updatedAt: '' },
];

describe('InventoryListPageComponent', () => {
  let fixture: ComponentFixture<InventoryListPageComponent>;
  let component: InventoryListPageComponent;
  let durableApi: StubDurableApi;
  let consumableApi: StubConsumableApi;

  beforeEach(async () => {
    durableApi = new StubDurableApi();
    consumableApi = new StubConsumableApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryDurableApiService, useValue: durableApi },
        { provide: InventoryConsumableApiService, useValue: consumableApi },
        { provide: InventoryAreaApiService, useValue: new StubAreaApi() },
        { provide: InventoryBrandApiService, useValue: new StubBrandApi() },
        { provide: InventoryTagApiService, useValue: new StubTagApi() },
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

  // Flush the async list/area fetches.
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

  it('selecting an area drives the server fetch (AreaId param) rather than a client-side pass', async () => {
    component.areaFilter.set(GARAGE);
    await settle();

    // The area scope goes to BOTH fetches as AreaId (resolved on the server), not Unplaced.
    expect(durableApi.lastParams?.AreaId).toBe(GARAGE);
    expect(durableApi.lastParams?.Unplaced).toBeUndefined();
    expect(consumableApi.lastParams?.AreaId).toBe(GARAGE);

    // scopedRows just reflects the server-filtered payload — nested items + stock resolving to Garage,
    // including screws (its second entry sits inside the chest container). Zero-stock glue is absent.
    expect(ids(scopedRows())).toEqual(['archived', 'bin', 'chest', 'ladder', 'screw', 'screws', 'tape', 'wrench']);
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('selecting "unplaced" drives the server fetch (Unplaced param)', async () => {
    component.areaFilter.set('unplaced');
    await settle();

    expect(durableApi.lastParams?.Unplaced).toBe(true);
    expect(durableApi.lastParams?.AreaId).toBeUndefined();
    expect(consumableApi.lastParams?.Unplaced).toBe(true);

    expect(ids(scopedRows())).toEqual(['floating', 'loose']);
    expect(scopedRows().some(r => r.id === 'glue')).toBe(false);
  });

  it('sends neither area param for the "all" scope', () => {
    expect(durableApi.lastParams?.AreaId).toBeUndefined();
    expect(durableApi.lastParams?.Unplaced).toBeUndefined();
    expect(consumableApi.lastParams?.AreaId).toBeUndefined();
  });

  it('selecting a brand drives the server fetch (BrandId param) on both kinds', async () => {
    component.selectBrandFilter(APPLE);
    await settle();

    expect(durableApi.lastParams?.BrandId).toBe(APPLE);
    expect(consumableApi.lastParams?.BrandId).toBe(APPLE);
    // Only the Apple-branded rows survive, across both kinds.
    expect(ids(scopedRows())).toEqual(['tape', 'wrench']);
  });

  it('sends no BrandId for the "all" brand scope', () => {
    expect(durableApi.lastParams?.BrandId).toBeUndefined();
    expect(consumableApi.lastParams?.BrandId).toBeUndefined();
  });

  it('the brand filter is orthogonal to search and area (all three go to the fetch together)', async () => {
    component.areaFilter.set(GARAGE);
    component.selectBrandFilter(APPLE);
    await settle();

    expect(durableApi.lastParams?.AreaId).toBe(GARAGE);
    expect(durableApi.lastParams?.BrandId).toBe(APPLE);
    // Picking a brand does not clear the area scope.
    expect(component.areaFilter()).toBe(GARAGE);
  });

  it('applies a ?brand= that arrives while already mounted (quick-panel link) and closes the panel', async () => {
    // A brand link inside a quick panel overlaid on the list navigates to ?brand= without
    // reconstructing the component; the reactive handler must still apply it, close the panel,
    // and strip the param.
    component.onOpenDurable(WRENCH);
    expect(component.durableQuickPanelState()).toBe('open');

    await TestBed.inject(Router).navigate([], { queryParams: { brand: APPLE } });
    await settle();

    expect(component.brandFilter()).toBe(APPLE);
    expect(component.durableQuickPanelState()).toBe('closed');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['brand']).toBeUndefined();
    expect(ids(scopedRows())).toEqual(['tape', 'wrench']);
  });

  it('onReset clears the brand scope too', async () => {
    component.selectBrandFilter(APPLE);
    await settle();
    expect(component.brandFilter()).toBe(APPLE);

    component.onReset();
    await settle();
    expect(component.brandFilter()).toBe('all');
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);
  });

  it('shows a "Showing X of N" notice when a kind\'s total exceeds the rows shown', async () => {
    durableApi.totalCountOverride = 250; // server reports more than the 200-row page returned
    component.onChanged(); // re-fetch with the inflated total
    await settle();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(`Showing ${ITEMS.length} of 250 durables`);
    expect(text).toContain('refine with search');
  });

  it('routes a container "View contents" to the Browse container drill (list no longer drills)', () => {
    const navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.onViewContents(durableToRow(CHEST));
    expect(navSpy).toHaveBeenCalledWith(['/inventory/browse/container', 'chest']);

    navSpy.mockClear();
    component.onQuickPanelViewContents(CHEST);
    expect(navSpy).toHaveBeenCalledWith(['/inventory/browse/container', 'chest']);
  });

  it('exposes the area scope as the items-view trigger label', () => {
    expect(component['scopeLabel']()).toBeNull();
    component.areaFilter.set(GARAGE);
    expect(component['scopeLabel']()).toBe('Filter: Garage');
    component.areaFilter.set('unplaced');
    expect(component['scopeLabel']()).toBe('Filter: Unplaced');
  });

  // Drive the committed term the way the app does — through the URL.
  async function navigateSearch(term: string | null): Promise<void> {
    await TestBed.inject(Router).navigate([], { queryParams: { search: term } });
    await settle();
  }

  // Let the 300ms searchInput debounce fire and the resulting navigation/fetch settle. The margin
  // over 300ms is generous on purpose: these are real timers, and the page's other debounced
  // pipelines (tagOptions/tagChips) plus CPU contention under the build gate can otherwise steal the
  // tail of a tight window and flake the commit assertion.
  async function flushDebounce(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, 700));
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
    expect((fixture.nativeElement as HTMLElement).querySelector('app-search-input')).not.toBeNull();
  });

  it('committing a typed search drops the area scope and syncs the URL', async () => {
    component.areaFilter.set(GARAGE);
    component.searchInput.set('screw');
    await flushDebounce();

    expect(component.areaFilter()).toBe('all');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
    expect(ids(scopedRows())).toEqual(['screw', 'screws']);
  });

  it('re-committing the same term immediately after a programmatic clear still searches', async () => {
    await navigateSearch('screw');
    component.selectAreaFilter(GARAGE); // clears search, scopes to an area
    await settle();
    expect(component.searchInput()).toBe('');

    // Re-typed before the clear's own debounce window closes — must not be swallowed.
    component.searchInput.set('screw');
    await flushDebounce();
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBe('screw');
    // Committing search is a global scope, so it drops the area filter.
    expect(component.areaFilter()).toBe('all');
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

  it('onReset clears the committed search along with the area scope', async () => {
    await navigateSearch('screw');
    component.areaFilter.set(GARAGE);

    component.onReset();
    await settle();
    expect(component.searchInput()).toBe('');
    expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['search']).toBeUndefined();
    expect(component.areaFilter()).toBe('all');
    expect(scopedRows().length).toBe(ITEMS.length + CONSUMABLES.length);
  });

  // Deep links from the overview tiles and place cards: params seed the items-view (kind/low/lent)
  // and the page's own area scope once at construction, and are then stripped from the URL. Container
  // navigation lives on Browse, so ?container= is no longer a list deep link.
  describe('deep-link param seeding', () => {
    async function createWithParams(params: Record<string, string | string[]>): Promise<ComponentFixture<InventoryListPageComponent>> {
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

    it('?brand seeds the page brand scope and strips the param (detail-page chip deep link)', async () => {
      const f = await createWithParams({ brand: APPLE });
      expect(f.componentInstance.brandFilter()).toBe(APPLE);
      expect(ids(f.componentInstance['scopedRows']())).toEqual(['tape', 'wrench']);
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['brand']).toBeUndefined();
    });

    it('?tagIds (+?includeChildTags) seed the page tag scope on both fetches and strip the params', async () => {
      const f = await createWithParams({ tagIds: ['t-a', 't-b'], includeChildTags: 'true' });

      expect(f.componentInstance.tagFilter()).toEqual(new Set(['t-a', 't-b']));
      expect(f.componentInstance.includeChildTags()).toBe(true);
      // Both server fetches carry the resolved tag scope (same expanded id set + toggle).
      expect(durableApi.lastParams?.TagIds).toEqual(['t-a', 't-b']);
      expect(consumableApi.lastParams?.TagIds).toEqual(['t-a', 't-b']);
      expect(durableApi.lastParams?.IncludeChildTags).toBe(true);
      expect(consumableApi.lastParams?.IncludeChildTags).toBe(true);
      const stripped = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(stripped['tagIds']).toBeUndefined();
      expect(stripped['includeChildTags']).toBeUndefined();
    });

    it('an unknown ?kind value falls back to all kinds', async () => {
      const f = await createWithParams({ kind: 'bogus' });
      expect(itemsView(f).kind()).toBe('all');
    });

    it('param stripping preserves an accompanying ?search', async () => {
      const f = await createWithParams({ lowStock: 'true', search: 'screw' });
      expect(itemsView(f).lowStockOnly()).toBe(true);
      const params = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(params['lowStock']).toBeUndefined();
      expect(params['search']).toBe('screw');
    });

    it('?sort=updatedAt&dir=desc seeds the items-view sort and strips the params', async () => {
      const f = await createWithParams({ sort: 'updatedAt', dir: 'desc' });
      expect(itemsView(f).sort()).toEqual({ id: 'updatedAt', desc: true });
      const stripped = TestBed.inject(Router).routerState.snapshot.root.queryParams;
      expect(stripped['sort']).toBeUndefined();
      expect(stripped['dir']).toBeUndefined();
    });

    it('an unknown ?sort value falls back to name-ascending', async () => {
      const f = await createWithParams({ sort: 'bogus', dir: 'desc' });
      expect(itemsView(f).sort()).toEqual({ id: 'name', desc: false });
    });

    it('?sort without ?dir defaults to ascending', async () => {
      const f = await createWithParams({ sort: 'updatedAt' });
      expect(itemsView(f).sort()).toEqual({ id: 'updatedAt', desc: false });
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

  describe('tag filter facet', () => {
    it('applies a ?tagIds= that arrives while already mounted (quick-panel link) and closes the panel', async () => {
      // A tag badge/CRUD link inside a quick panel overlaid on the list navigates to ?tagIds=
      // without reconstructing the component; the reactive handler must apply it, close the panel,
      // and strip the param.
      component.onOpenDurable(WRENCH);
      expect(component.durableQuickPanelState()).toBe('open');

      await TestBed.inject(Router).navigate([], { queryParams: { tagIds: 't-x' } });
      await settle();

      expect(component.tagFilter().has('t-x')).toBe(true);
      expect(component.durableQuickPanelState()).toBe('closed');
      expect(TestBed.inject(Router).routerState.snapshot.root.queryParams['tagIds']).toBeUndefined();
    });

    it('resolves the selected tag ids to chip labels via getTagsByIds', async () => {
      component.tagFilter.set(new Set(['t-a']));
      await settle();
      expect(component['tagChips']()).toEqual([{ id: 't-a', path: 'tag/t-a' }]);
    });

    it('toggleTagFilter flips set membership', () => {
      component.toggleTagFilter('t-a');
      expect(component.tagFilter().has('t-a')).toBe(true);
      component.toggleTagFilter('t-a');
      expect(component.tagFilter().has('t-a')).toBe(false);
    });

    it('onReset clears the tag scope and the sub-tag toggle', async () => {
      component.tagFilter.set(new Set(['t-a']));
      component.includeChildTags.set(true);
      await settle();

      component.onReset();
      await settle();
      expect(component.tagFilter().size).toBe(0);
      expect(component.includeChildTags()).toBe(false);
    });

    it('extraFiltersActive is true when only a tag filter is active', () => {
      expect(component['extraFiltersActive']()).toBe(false);
      component.tagFilter.set(new Set(['t-a']));
      expect(component['extraFiltersActive']()).toBe(true);
    });

    it('does not render the "No items yet" empty state while a tag filter is active', async () => {
      // The tag filter returns zero rows (fixtures carry no tags); the empty-state guard's
      // `tagFilter().size === 0` clause must keep "No items yet" hidden and show the filtered state.
      component.tagFilter.set(new Set(['t-a']));
      await settle();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).not.toContain('No items yet');
      expect(text).toContain('Nothing matches');
    });
  });
});
