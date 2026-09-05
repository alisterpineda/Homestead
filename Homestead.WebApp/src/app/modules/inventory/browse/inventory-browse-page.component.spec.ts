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
  expiringWarrantyCount: 0,
  expiringWarranties: [],
  lowStockCount: 0,
  lowStockConsumables: [],
  // Lent is folded into the needs-attention panel, so a household-wide lent item opens it at root
  // even with nothing low.
  lentCount: 1,
  lentDurables: [{ id: 'ladder', name: 'Ladder', statusNote: 'Dave next door' }],
  unplacedCount: 2,
  areas: [
    { id: 'garage', name: 'Garage', itemCount: 12, lowStockCount: 3 },
    { id: 'office', name: 'Office', itemCount: 0, lowStockCount: 0 },
  ],
  recentlyUpdated: [],
};

const GARAGE_CONTENTS: PlaceContentsResponse = {
  path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
  containerItem: null,
  scopedLowStockCount: 0,
  scopedLentCount: 0,
  childAreas: [],
  containers: [
    { id: 'chest', name: 'Tool chest', status: 'Active', itemCount: 1, lowStockCount: 0, previewNames: ['Wrench'] },
  ],
  items: [{
    id: 'drill', name: 'Drill', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '',
  }],
  consumables: [],
};

// A three-level nested-area drill (House › Basement › Bedroom), for exercising the multi-segment
// breadcrumb: ancestor area links, last segment as plain text. (Containers also drill within Browse,
// so a breadcrumb can mix area and container segments; this fixture keeps it all-area for focus.)
const NESTED_AREA_CONTENTS: PlaceContentsResponse = {
  path: [
    { id: 'house', name: 'House', kind: 'Area' },
    { id: 'basement', name: 'Basement', kind: 'Area' },
    { id: 'bedroom', name: 'Bedroom', kind: 'Area' },
  ],
  containerItem: null,
  scopedLowStockCount: 0,
  scopedLentCount: 0,
  childAreas: [],
  containers: [],
  items: [],
  consumables: [],
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

/** Stand-in for the real durable detail page — the target of a container's opt-in "View details" button. */
@Component({
  selector: 'test-item-detail-stub',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
})
class ItemDetailStubComponent {}

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
          // Containers drill within Browse (Explore); the opt-in "View details" button routes to the detail page.
          { path: 'inventory/browse/container/:containerId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'container' } },
          { path: 'inventory/browse/unplaced', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'unplaced' } },
          { path: 'inventory/items/:id', component: ItemDetailStubComponent },
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
    expect((await go(harness, '/inventory/browse/container/chest')).placeRef())
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

  it('surfaces lent inside the needs-attention panel at the root, even with nothing low', async () => {
    // OVERVIEW has lowStockCount: 0 but lentCount: 1 — lent is folded into needs-attention, so the
    // panel opens on lent alone with a "Lent out" tile deep-linking into the items list.
    const { harness } = await navigate('/inventory/browse');
    const content = text(harness);
    expect(content).toContain('Needs attention');
    expect(content).toContain('Lent out');
    expect(content).toContain('Ladder');
    expect(hrefs(harness)).toContain('/inventory/items?lent=true');
  });

  it('surfaces the low-stock alert at the root when something is running low', async () => {
    api.response = {
      ...OVERVIEW,
      lowStockCount: 1,
      lowStockConsumables: [{ id: 'glue', name: 'Glue', totalOnHand: 0, unit: 'tubes', restockThreshold: 5 }],
    };
    const { harness } = await navigate('/inventory/browse');
    expect(text(harness)).toContain('Needs attention');
    expect(hrefs(harness)).toContain('/inventory/items?lowStock=true');
  });

  it('renders the area-scoped warning panel when the drilled place has warnings', async () => {
    placeApi.contents = { ...GARAGE_CONTENTS, scopedLowStockCount: 2, scopedLentCount: 0 };
    const { harness } = await navigate('/inventory/browse/area/garage');

    expect(text(harness)).toContain('Low consumables stored here');
    expect(hrefs(harness)).toContain('/inventory/items?area=garage&lowStock=true');
  });

  it('renders a count-only lent panel when drilled into an area with lent items', async () => {
    placeApi.contents = { ...GARAGE_CONTENTS, scopedLowStockCount: 0, scopedLentCount: 2 };
    const { harness } = await navigate('/inventory/browse/area/garage');

    const content = text(harness);
    // Lent folds into needs-attention; while drilled it's count-only (no items-list deep-link) — the
    // lent items already show in the contents table below.
    expect(content).toContain('Needs attention');
    expect(content).toContain('Items lent from here');
    expect(hrefs(harness)).not.toContain('/inventory/items?lent=true');
    expect(hrefs(harness)).not.toContain('/inventory/items?lent=true&area=garage');
  });

  it('drills into an area: fetches contents and renders the drilled view', async () => {
    const { harness } = await navigate('/inventory/browse/area/garage');

    expect(placeApi.calls).toEqual([{ AreaId: 'garage' }]);
    const content = text(harness);
    expect(content).toContain('Tool chest');
    expect(content).toContain('Drill');
    // The drilled child-container card continues spatial drilling within Browse (Explore).
    expect(hrefs(harness)).toContain('/inventory/browse/container/chest');
  });

  it('drills into a container: fetches its contents by ContainerItemId', async () => {
    placeApi.contents = {
      ...GARAGE_CONTENTS,
      path: [
        { id: 'garage', name: 'Garage', kind: 'Area' },
        { id: 'chest', name: 'Tool chest', kind: 'Container' },
      ],
      containerItem: {
        id: 'chest', name: 'Tool chest', fungibility: 'NonFungible', status: 'Active',
        isContainer: true, path: [], createdAt: '', updatedAt: '',
      },
    };
    const { harness } = await navigate('/inventory/browse/container/chest');

    expect(placeApi.calls).toEqual([{ ContainerItemId: 'chest' }]);
    // Still on the Browse surface — the container renders as a drilled place, with its View-details button.
    expect(text(harness)).toContain('Tool chest');
  });

  it('steps into the container record via onContainerDetails (the opt-in Explore→Detail step)', async () => {
    const { component } = await navigate('/inventory/browse/container/chest');
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.onContainerDetails({
      id: 'chest', name: 'Tool chest', fungibility: 'NonFungible', status: 'Active',
      isContainer: true, path: [], createdAt: '', updatedAt: '',
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items', 'chest']);
  });

  it('keeps drilling within Browse when a loose container row asks to view contents', async () => {
    const { component } = await navigate('/inventory/browse/container/chest');
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.onViewContainer('tray');

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/browse/container', 'tray']);
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

  it('renders the registered breadcrumb: ancestor area links, last as plain text', async () => {
    placeApi.contents = NESTED_AREA_CONTENTS;
    const harness = await RouterTestingHarness.create();
    await go(harness, '/inventory/browse/area/bedroom');

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

    // Root crumb + the two area ancestors, each targeting its area drill route.
    expect(linksByText.get('Browse')).toBe('/inventory/browse');
    expect(linksByText.get('House')).toBe('/inventory/browse/area/house');
    expect(linksByText.get('Basement')).toBe('/inventory/browse/area/basement');

    // The drilled node itself is the current page — plain text, not a link.
    expect(linksByText.has('Bedroom')).toBe(false);
    const current = el.querySelector('[aria-current="page"]');
    expect(current?.textContent?.trim()).toBe('Bedroom');
  });

  it('renders container-scoped low-stock and lent alerts count-only (no links) using the backend DIRECT counts', async () => {
    // B10: for a container scope the backend computes DIRECT counts server-side (its own contents, not
    // the rolled subtree), and the component displays scopedLowStockCount/scopedLentCount verbatim
    // rather than recomputing from the rows. Proven here by a lent CHILD CONTAINER that the old
    // `items.filter` recompute could not see (child containers render as container summaries, not
    // items): the backend lent count (3) exceeds the two lent durables in `items`. Both alerts render
    // count-only — the items list can't scope to a container (Browse owns containment), so the matching
    // rows already appear in the contents table below.
    placeApi.contents = {
      ...GARAGE_CONTENTS,
      path: [{ id: 'chest', name: 'Tool chest', kind: 'Container' }],
      containerItem: {
        id: 'chest', name: 'Tool chest', fungibility: 'NonFungible', status: 'Active',
        isContainer: true, path: [], createdAt: '', updatedAt: '',
      },
      scopedLowStockCount: 1,
      scopedLentCount: 3, // 2 lent durables below + 1 lent child container the row recompute couldn't see
      containers: [
        { id: 'bin', name: 'Lent bin', status: 'Lent', itemCount: 0, lowStockCount: 0, previewNames: [] },
      ],
      items: [
        { id: 'ladder', name: 'Ladder', fungibility: 'NonFungible', status: 'Lent', isContainer: false, path: [], createdAt: '', updatedAt: '' },
        { id: 'saw', name: 'Saw', fungibility: 'NonFungible', status: 'Lent', isContainer: false, path: [], createdAt: '', updatedAt: '' },
        { id: 'drill', name: 'Drill', fungibility: 'NonFungible', status: 'Active', isContainer: false, path: [], createdAt: '', updatedAt: '' },
      ],
      consumables: [{
        consumable: {
          id: 'screws', name: 'Wood screws', status: 'Active', unit: 'pcs',
          totalOnHand: 4, isLow: true, createdAt: '', updatedAt: '', stockLocations: [],
        },
        localQuantity: 4,
      }],
    };
    const { harness } = await navigate('/inventory/browse/container/chest');

    const content = text(harness);
    expect(content).toContain('Low consumables stored here');
    expect(content).toContain('Items lent from here');
    // Displays the backend's authoritative DIRECT count (3), not the two lent rows a client recompute
    // would see — the lent child container is included.
    expect(content).toContain('3 lent');
    // No list deep-links from a container drill — both alerts are count-only here.
    expect(hrefs(harness).some(h => h.includes('container=chest'))).toBe(false);
    expect(hrefs(harness)).not.toContain('/inventory/items?lent=true');
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
