import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { InventoryOverviewPageComponent } from './inventory-overview-page.component';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

// The hosted quick panels and create dialog use pickers backed by ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

const ALL_QUIET: GetInventoryOverviewResponse = {
  lowStockCount: 0,
  lowStockSupplies: [],
  lentCount: 0,
  lentItems: [],
  unplacedCount: 0,
  unplacedItems: [],
  areas: [],
};

const BUSY: GetInventoryOverviewResponse = {
  lowStockCount: 7,
  lowStockSupplies: [
    { id: 'glue', name: 'Glue', totalOnHand: 0, unit: 'tubes', restockThreshold: 5 },
    { id: 'tape', name: 'Tape', totalOnHand: 2, unit: 'rolls', restockThreshold: 3 },
  ],
  lentCount: 1,
  lentItems: [{ id: 'ladder', name: 'Ladder', lentTo: 'Dave next door' }],
  unplacedCount: 2,
  unplacedItems: [
    { id: 'wrench', name: 'Adrift wrench' },
    { id: 'bin', name: 'Floating bin' },
  ],
  areas: [
    { id: 'garage', name: 'Garage', itemCount: 12, lowStockCount: 3 },
    { id: 'office', name: 'Office', itemCount: 0, lowStockCount: 0 },
  ],
};

const GARAGE_CONTENTS: PlaceContentsResponse = {
  path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
  containerItem: null,
  directItemCount: 2,
  nestedItemCount: 1,
  containers: [
    { id: 'chest', name: 'Tool chest', status: 'Active', itemCount: 1, lowStockCount: 0, previewNames: ['Wrench'] },
  ],
  items: [{
    id: 'drill', name: 'Drill', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '',
  }],
  supplies: [],
};

class StubOverviewApi {
  response: GetInventoryOverviewResponse = ALL_QUIET;
  fetches = 0;
  getOverview = () => {
    this.fetches++;
    return of(this.response);
  };
}

class StubPlaceApi {
  contents: PlaceContentsResponse = GARAGE_CONTENTS;
  calls: Record<string, unknown>[] = [];
  getPlaceContents = (params: Record<string, unknown>) => {
    this.calls.push(params);
    return of(this.contents);
  };
}

describe('InventoryOverviewPageComponent', () => {
  let api: StubOverviewApi;
  let placeApi: StubPlaceApi;

  beforeEach(() => {
    api = new StubOverviewApi();
    placeApi = new StubPlaceApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryOverviewApiService, useValue: api },
        { provide: InventoryPlaceApiService, useValue: placeApi },
      ],
    });
  });

  async function create(
    queryParams?: Record<string, string>,
  ): Promise<ComponentFixture<InventoryOverviewPageComponent>> {
    if (queryParams) {
      await TestBed.inject(Router).navigate([], { queryParams });
    }
    const fixture = TestBed.createComponent(InventoryOverviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<InventoryOverviewPageComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function links(fixture: ComponentFixture<InventoryOverviewPageComponent>): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  it('renders the tile counts and preview rows', async () => {
    api.response = BUSY;
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('Low stock');
    expect(content).toContain('Lent out');
    expect(content).toContain('Unplaced');
    expect(content).toContain('Glue');
    expect(content).toContain('0 tubes / 5');
    expect(content).toContain('Ladder');
    expect(content).toContain('Dave next door');
    expect(content).toContain('Adrift wrench');
    // 7 low-stock total with 2 previewed → "+5 more".
    expect(content).toContain('+5 more');
  });

  it('deep-links each tile to the pre-filtered items list', async () => {
    api.response = BUSY;
    const fixture = await create();

    expect(links(fixture)).toContain('/inventory/items?lowStock=true');
    expect(links(fixture)).toContain('/inventory/items?lent=true');
    expect(links(fixture)).toContain('/inventory/items?area=unplaced&kind=items');
  });

  it('shows per-tile all-good empty states with no view-all links', async () => {
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('Nothing running low');
    expect(content).toContain('Nothing lent out');
    expect(content).toContain('Everything has a place');
    // The only link on an all-quiet dashboard is the browse-by-place empty-state hint.
    expect(links(fixture)).toEqual(['/inventory/areas']);
  });

  it('renders browse-by-place cards that drill in place via ?place=', async () => {
    api.response = BUSY;
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('Browse by place');
    expect(content).toContain('Garage');
    expect(content).toContain('12');
    expect(content).toContain('Office');

    expect(links(fixture)).toContain('/?place=area:garage');
    expect(links(fixture)).toContain('/?place=area:office');
    // Drilling is in place — area cards no longer navigate to the items list.
    expect(links(fixture)).not.toContain('/inventory/items?area=garage');
  });

  it('shows the Unplaced card only when there are unplaced items', async () => {
    api.response = BUSY;
    let fixture = await create();
    expect(links(fixture)).toContain('/?place=unplaced');

    api.response = { ...BUSY, unplacedCount: 0, unplacedItems: [] };
    fixture = await create();
    expect(links(fixture)).not.toContain('/?place=unplaced');
    // Area cards still render without the Unplaced card.
    expect(links(fixture)).toContain('/?place=area:garage');
  });

  it('shows an empty hint linking to the Areas page when there is nothing to browse', async () => {
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('No areas yet');
    const hint = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a[href="/inventory/areas"]',
    );
    expect(hint).not.toBeNull();
  });

  it('shows the error state when the fetch fails', async () => {
    api.getOverview = () => throwError(() => new Error('boom'));
    const fixture = await create();

    expect(text(fixture)).toContain('Failed to load overview');
  });

  it('does not fetch place contents at root', async () => {
    api.response = BUSY;
    await create();

    expect(placeApi.calls).toEqual([]);
  });

  it('?place=area:<id> drills in: fetches contents and renders the drilled view', async () => {
    api.response = BUSY;
    const fixture = await create({ place: 'area:garage' });

    expect(placeApi.calls).toEqual([{ AreaId: 'garage' }]);
    const content = text(fixture);
    expect(content).toContain('All places');
    expect(content).toContain('2 here · 1 more inside containers');
    expect(content).toContain('Tool chest');
    expect(content).toContain('Drill');
    expect(content).toContain('Open as list');
  });

  it('?place=unplaced fetches with the Unplaced flag', async () => {
    api.response = BUSY;
    await create({ place: 'unplaced' });

    expect(placeApi.calls).toEqual([{ Unplaced: true }]);
  });

  it('a malformed ?place falls back to the root grid without fetching', async () => {
    api.response = BUSY;
    const fixture = await create({ place: 'garbage' });

    expect(placeApi.calls).toEqual([]);
    expect(text(fixture)).toContain('Browse by place');
  });

  it('hides the attention tiles but keeps search visible while drilled', async () => {
    api.response = BUSY;
    const fixture = await create({ place: 'area:garage' });

    const content = text(fixture);
    expect(content).not.toContain('Lent out');
    expect(content).not.toContain('Low stock');
    const search = (fixture.nativeElement as HTMLElement)
      .querySelector('input[aria-label="Search items and supplies"]');
    expect(search).not.toBeNull();
  });

  it('refetches the overview and the drilled contents on changed', async () => {
    api.response = BUSY;
    const fixture = await create({ place: 'area:garage' });
    expect(api.fetches).toBe(1);
    expect(placeApi.calls.length).toBe(1);

    fixture.componentInstance.onChanged();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(api.fetches).toBe(2);
    expect(placeApi.calls.length).toBe(2);
  });

  it('hands the search term off to the items list on submit', async () => {
    const fixture = await create();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.componentInstance.searchInput.set('  drill ');
    fixture.componentInstance.onSearchSubmit(new Event('submit'));

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items'], {
      queryParams: { search: 'drill' },
    });
  });

  it('navigates with a null search param when the box is empty', async () => {
    const fixture = await create();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.componentInstance.onSearchSubmit(new Event('submit'));

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items'], {
      queryParams: { search: null },
    });
  });
});
