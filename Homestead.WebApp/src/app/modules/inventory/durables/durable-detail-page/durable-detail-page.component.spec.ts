import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { DurableDetailPageComponent } from './durable-detail-page.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';
import type { GetDurableActivityResponse } from '@/api/generated/models/get-durable-activity-response';

// The hosted edit sheet / quick panel host pickers backed by ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

const LEAF_ITEM: DurableResponse = {
  id: 'item1',
  name: 'Cordless Drill',
  fungibility: 'NonFungible',
  status: 'Active',
  isContainer: false,
  path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
  resolvedAreaId: 'garage',
  areaId: 'garage',
  areaName: 'Garage',
  value: 120,
  valueCurrencySymbol: '$',
  serial: 'SN-123',
  createdAt: '2025-01-01T00:00:00Z',
  updatedAt: '2025-06-01T00:00:00Z',
};

const CONTAINER_ITEM: DurableResponse = {
  id: 'box1',
  name: 'Tool Chest',
  fungibility: 'NonFungible',
  status: 'Active',
  isContainer: true,
  path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
  resolvedAreaId: 'garage',
  createdAt: '2025-01-01T00:00:00Z',
  updatedAt: '2025-06-01T00:00:00Z',
};

const CHEST_CONTENTS: PlaceContentsResponse = {
  path: [
    { id: 'garage', name: 'Garage', kind: 'Area' },
    { id: 'box1', name: 'Tool Chest', kind: 'Container' },
  ],
  containerItem: CONTAINER_ITEM,
  scopedLowStockCount: 0,
  scopedLentCount: 0,
  childAreas: [],
  containers: [
    { id: 'tray', name: 'Tray', status: 'Active', itemCount: 3, lowStockCount: 0, previewNames: [] },
  ],
  items: [{
    id: 'wrench', name: 'Wrench', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '2025-05-01T00:00:00Z',
  }],
  consumables: [],
};

class StubDurableApi {
  item: DurableResponse = LEAF_ITEM;
  getDurableError = false;
  getDurableCalls = 0;
  patchCalls: { id: string; body: unknown }[] = [];
  deleteCalls: string[] = [];

  getDurable = (params: { id: string }) => {
    this.getDurableCalls++;
    return this.getDurableError ? throwError(() => new Error('not found')) : of(this.item);
  };
  activityCalls: { id: string; Page: number; PageSize: number }[] = [];
  getDurableActivity = (p: { id: string; Page: number; PageSize: number }) => {
    this.activityCalls.push(p);
    return of({ items: [] as GetDurableActivityResponse[], page: p.Page, pageSize: p.PageSize, totalCount: 0, totalPages: 0 });
  };
  // Used by the hosted quick panel's contents-count probe (containers only).
  getDurables = () => of({ items: [], page: 1, pageSize: 1, totalCount: 0 });
  patchDurable = (params: { id: string; body: unknown }) => {
    this.patchCalls.push(params);
    return of(this.item);
  };
  deleteDurable = (params: { id: string }) => {
    this.deleteCalls.push(params.id);
    return of(undefined);
  };
}

class StubPlaceApi {
  contents: PlaceContentsResponse = CHEST_CONTENTS;
  calls: Record<string, unknown>[] = [];
  getPlaceContents = (params: Record<string, unknown>) => {
    this.calls.push(params);
    return of(this.contents);
  };
}

describe('DurableDetailPageComponent', () => {
  let durableApi: StubDurableApi;
  let placeApi: StubPlaceApi;

  beforeEach(() => {
    durableApi = new StubDurableApi();
    placeApi = new StubPlaceApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'inventory/items/:id', component: DurableDetailPageComponent, data: { title: 'Durable' } },
        ]),
        { provide: InventoryDurableApiService, useValue: durableApi },
        { provide: InventoryPlaceApiService, useValue: placeApi },
      ],
    });
  });

  async function navigate(url: string): Promise<{
    harness: RouterTestingHarness;
    component: DurableDetailPageComponent;
  }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, DurableDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
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

  function buttons(harness: RouterTestingHarness): HTMLButtonElement[] {
    return Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
  }

  it('renders the header with name, status, headline location, value, and serial', async () => {
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('Cordless Drill');
    // Active is the resting default state — it renders no status badge anywhere (see
    // ItemStatusBadgeComponent). A non-default status still shows its chip (asserted below).
    expect(content).not.toContain('Active');
    expect(content).toContain('Garage'); // resolved location, now the headline answer
    expect(content).toContain('120'); // value (Details box — present because value is set)
    expect(content).toContain('SN-123'); // serial
    // The Details box renders here because value/serial are set; back-navigation is the shared
    // `Items › name` breadcrumb, which lives on the dispatcher (covered by its spec), not this child.
    expect(content).toContain('Details');
    // A leaf durable shows no Contents section and never fetches place contents.
    expect(content).not.toContain('Contents');
    expect(placeApi.calls).toEqual([]);
  });

  it('renders the status chip in the header for a non-default status', async () => {
    durableApi.item = { ...LEAF_ITEM, status: 'Lent' };
    const { harness } = await navigate('/inventory/items/item1');

    // Non-default statuses are exactly the ones worth surfacing — the chip renders with its label.
    expect(text(harness)).toContain('Lent');
  });

  it('leads with the location headline and hides the Details box when no detail fields are set', async () => {
    // A bare leaf durable: placed, but no value/serial/acquired/lent. The common shape in current data.
    durableApi.item = {
      id: 'bare', name: 'Some Container', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
      resolvedAreaId: 'garage', createdAt: '', updatedAt: '2025-06-01T00:00:00Z',
    };
    const { harness } = await navigate('/inventory/items/bare');

    const content = text(harness);
    expect(content).toContain('Garage'); // location still leads as the headline
    expect(content).not.toContain('Details'); // empty Details box is hidden entirely
  });

  it('shows Unplaced as the location headline for an unplaced durable', async () => {
    durableApi.item = {
      id: 'floating', name: 'Floating Thing', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [], createdAt: '', updatedAt: '2025-06-01T00:00:00Z',
    };
    const { harness } = await navigate('/inventory/items/floating');

    expect(text(harness)).toContain('Unplaced');
  });

  it('shows the not-found state when the item is missing', async () => {
    durableApi.getDurableError = true;
    const { harness, component } = await navigate('/inventory/items/missing');

    expect(component.notFound()).toBe(true);
    expect(text(harness)).toContain('Durable not found');
  });

  it('opens the edit sheet on Edit and refreshes the detail on save', async () => {
    const { harness, component } = await navigate('/inventory/items/item1');
    expect(durableApi.getDurableCalls).toBe(1);

    buttons(harness).find(b => b.textContent?.trim() === 'Edit')!.click();
    harness.detectChanges();
    expect(component.editState()).toBe('open');
    expect(component.editDurable()?.id).toBe('item1');

    component.onEditSaved();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(component.editState()).toBe('closed');
    expect(durableApi.getDurableCalls).toBe(2);
  });

  it('renders the contents list for a container, drilling child containers into their detail page', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.contents = CHEST_CONTENTS;
    const { harness } = await navigate('/inventory/items/box1');

    expect(placeApi.calls).toEqual([{ ContainerItemId: 'box1' }]);
    const content = text(harness);
    expect(content).toContain('Contents');
    expect(content).toContain('Tray'); // child container
    expect(content).toContain('Wrench'); // leaf item
    expect(hrefs(harness)).toContain('/inventory/items/tray');
  });

  it('renders the activity history feed with each summary', async () => {
    durableApi.getDurableActivity = () => of({
      items: [
        { id: 'a1', summary: 'Alice updated value, location', timestamp: '2025-06-01T00:00:00Z', userDisplayName: 'Alice', userEmail: null },
        { id: 'a2', summary: 'Someone added this item', timestamp: '2025-05-01T00:00:00Z', userDisplayName: null, userEmail: null },
      ],
      page: 1, pageSize: 20, totalCount: 2, totalPages: 1,
    });
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('History');
    expect(content).toContain('Alice updated value, location');
    // A null/deleted actor still renders (summary says "Someone"), never vanishes.
    expect(content).toContain('Someone added this item');
  });

  it('resets the activity page and open overlays when navigating to another item (component reused)', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.contents = CHEST_CONTENTS;
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/inventory/items/box1', DurableDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();

    // Simulate user state: paged the history forward and opened the edit sheet + quick panel.
    component.page.set(2);
    component.editState.set('open');
    component.durableQuickPanelState.set('open');
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(durableApi.activityCalls.at(-1)).toMatchObject({ id: 'box1', Page: 2 });

    // Navigate to a different item via the same route (Angular reuses the component instance).
    durableApi.item = LEAF_ITEM;
    const component2 = await harness.navigateByUrl('/inventory/items/item1', DurableDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(component2).toBe(component); // confirms the reuse the fix guards against
    // page resets to 1, so the new item's history is fetched at page 1 — not the stale page 2.
    expect(component2.page()).toBe(1);
    expect(durableApi.activityCalls.at(-1)).toMatchObject({ id: 'item1', Page: 1 });
    // overlays opened for the previous item are closed, not left hanging over the new one.
    expect(component2.editState()).toBe('closed');
    expect(component2.durableQuickPanelState()).toBe('closed');
  });

  it('shows an error state (not a false empty) when the activity fetch fails', async () => {
    durableApi.getDurableActivity = () => throwError(() => new Error('boom'));
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain("Couldn't load history");
    expect(content).not.toContain('No activity recorded yet.');
  });

  it('shows an error state (not an infinite spinner) when a container contents fetch fails', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.getPlaceContents = () => throwError(() => new Error('boom'));
    const { harness } = await navigate('/inventory/items/box1');

    expect(text(harness)).toContain("Couldn't load contents");
  });

  it('opens the quick panel for a leaf item in the contents list', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.contents = CHEST_CONTENTS;
    const { harness, component } = await navigate('/inventory/items/box1');

    buttons(harness).find(b => b.textContent?.includes('Wrench'))!.click();
    harness.detectChanges();

    expect(component.durableQuickPanelState()).toBe('open');
    expect(component.quickPanelDurable()?.id).toBe('wrench');
  });
});
