import { ChangeDetectionStrategy, Component, inject, provideZonelessChangeDetection } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { InventoryBrowsePageComponent } from './inventory-browse-page.component';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
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

// A two-level container drill (Garage › Tool chest › Bin), for exercising the multi-segment
// breadcrumb: ancestor links derived from each segment's kind, last segment as plain text.
const BIN_CONTENTS: PlaceContentsResponse = {
  path: [
    { id: 'garage', name: 'Garage', kind: 'Area' },
    { id: 'chest', name: 'Tool chest', kind: 'Container' },
    { id: 'bin', name: 'Bin', kind: 'Container' },
  ],
  containerItem: {
    id: 'bin', name: 'Bin', fungibility: 'NonFungible', status: 'Active',
    isContainer: true, path: [], createdAt: '', updatedAt: '',
  },
  directItemCount: 0,
  nestedItemCount: 0,
  containers: [],
  items: [],
  supplies: [],
};

class StubOverviewApi {
  response: GetInventoryOverviewResponse = OVERVIEW;
  fetches = 0;
  getOverview = () => {
    this.fetches++;
    return of(this.response);
  };
}

/**
 * Renders whatever template the page registered as the header title — the breadcrumb lives in the
 * shell header (via PageHeaderService), not in the page's own DOM, so this stands in for the shell.
 */
@Component({
  selector: 'test-title-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `<ng-container [ngTemplateOutlet]="header.title()" />`,
})
class TitleHostComponent {
  protected header = inject(PageHeaderService);
}

class StubPlaceApi {
  contents: PlaceContentsResponse = GARAGE_CONTENTS;
  calls: Record<string, unknown>[] = [];
  getPlaceContents = (params: Record<string, unknown>) => {
    this.calls.push(params);
    return of(this.contents);
  };
}

describe('InventoryBrowsePageComponent', () => {
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
        provideRouter([
          { path: 'inventory/browse', component: InventoryBrowsePageComponent, data: { title: 'Browse' } },
          { path: 'inventory/browse/area/:areaId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'area' } },
          { path: 'inventory/browse/item/:itemId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'container' } },
          { path: 'inventory/browse/unplaced', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'unplaced' } },
        ]),
        { provide: InventoryOverviewApiService, useValue: api },
        { provide: InventoryPlaceApiService, useValue: placeApi },
      ],
    });
  });

  // RouterTestingHarness permits only one instance per test, so navigations within a test reuse it.
  async function go(
    harness: RouterTestingHarness,
    url: string,
  ): Promise<InventoryBrowsePageComponent> {
    const component = await harness.navigateByUrl(url, InventoryBrowsePageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    return component;
  }

  async function navigate(url: string): Promise<{
    harness: RouterTestingHarness;
    component: InventoryBrowsePageComponent;
  }> {
    const harness = await RouterTestingHarness.create();
    const component = await go(harness, url);
    return { harness, component };
  }

  function text(harness: RouterTestingHarness): string {
    return (harness.routeNativeElement as HTMLElement).textContent ?? '';
  }

  function hrefs(harness: RouterTestingHarness): string[] {
    return Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  it('derives placeRef from route data and params', async () => {
    const harness = await RouterTestingHarness.create();

    expect((await go(harness, '/inventory/browse')).placeRef()).toBeNull();
    expect((await go(harness, '/inventory/browse/area/garage')).placeRef())
      .toEqual({ kind: 'area', id: 'garage' });
    expect((await go(harness, '/inventory/browse/item/chest')).placeRef())
      .toEqual({ kind: 'container', id: 'chest' });
    expect((await go(harness, '/inventory/browse/unplaced')).placeRef())
      .toEqual({ kind: 'unplaced' });
  });

  it('renders the area grid with per-place links at root', async () => {
    const { harness } = await navigate('/inventory/browse');

    const content = text(harness);
    expect(content).toContain('Browse by place');
    expect(content).toContain('Garage');
    expect(content).toContain('Office');

    const links = hrefs(harness);
    expect(links).toContain('/inventory/browse/area/garage');
    expect(links).toContain('/inventory/browse/area/office');
    expect(links).toContain('/inventory/browse/unplaced');
    // Root fetches the overview for the grid, but never drills.
    expect(api.fetches).toBe(1);
    expect(placeApi.calls).toEqual([]);
  });

  it('drills into an area: fetches contents and renders the drilled view', async () => {
    const { harness } = await navigate('/inventory/browse/area/garage');

    expect(placeApi.calls).toEqual([{ AreaId: 'garage' }]);
    const content = text(harness);
    expect(content).toContain('2 here · 1 more inside containers');
    expect(content).toContain('Tool chest');
    expect(content).toContain('Drill');
    expect(content).toContain('Open as list');
    // The drilled child-container card routes one level deeper.
    expect(hrefs(harness)).toContain('/inventory/browse/item/chest');
  });

  it('drills into a container by id', async () => {
    await navigate('/inventory/browse/item/chest');
    expect(placeApi.calls).toEqual([{ ContainerItemId: 'chest' }]);
  });

  it('drills into Unplaced with the Unplaced flag', async () => {
    await navigate('/inventory/browse/unplaced');
    expect(placeApi.calls).toEqual([{ Unplaced: true }]);
  });

  it('does not fetch the overview when deep-linking to a drilled node', async () => {
    await navigate('/inventory/browse/area/garage');
    expect(api.fetches).toBe(0);
  });

  it('registers the header breadcrumb only while drilled', async () => {
    const header = TestBed.inject(PageHeaderService);
    const harness = await RouterTestingHarness.create();

    await go(harness, '/inventory/browse');
    expect(header.title()).toBeNull();

    await go(harness, '/inventory/browse/area/garage');
    expect(header.title()).not.toBeNull();
  });

  it('renders the registered breadcrumb: ancestor links by segment kind, last as plain text', async () => {
    placeApi.contents = BIN_CONTENTS;
    const harness = await RouterTestingHarness.create();
    await go(harness, '/inventory/browse/item/bin');

    // The breadcrumb is registered as the header title, not rendered in the page body — outlet it.
    const host = TestBed.createComponent(TitleHostComponent);
    host.detectChanges();
    await host.whenStable();
    host.detectChanges();

    const el = host.nativeElement as HTMLElement;
    const linksByText = new Map(
      Array.from(el.querySelectorAll<HTMLAnchorElement>('a[href]')).map(
        a => [a.textContent?.trim() ?? '', decodeURIComponent(a.getAttribute('href') ?? '')],
      ),
    );

    // Root crumb + the two ancestors, each targeting the route its segment KIND maps to.
    expect(linksByText.get('All places')).toBe('/inventory/browse');
    expect(linksByText.get('Garage')).toBe('/inventory/browse/area/garage'); // Area ancestor
    expect(linksByText.get('Tool chest')).toBe('/inventory/browse/item/chest'); // Container ancestor

    // The drilled node itself is the current page — plain text, not a link.
    expect(linksByText.has('Bin')).toBe(false);
    const current = el.querySelector('[aria-current="page"]');
    expect(current?.textContent?.trim()).toBe('Bin');
  });

  it('navigates one level deeper when the item panel asks to view contents', async () => {
    const { component } = await navigate('/inventory/browse/area/garage');
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.onQuickPanelViewContents({
      id: 'chest', name: 'Tool chest', fungibility: 'NonFungible', status: 'Active',
      isContainer: true, path: [], createdAt: '', updatedAt: '',
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/browse/item', 'chest']);
  });

  it('refetches the drilled contents on changed', async () => {
    const { component, harness } = await navigate('/inventory/browse/area/garage');
    expect(placeApi.calls.length).toBe(1);

    component.onChanged();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(placeApi.calls.length).toBe(2);
  });
});
