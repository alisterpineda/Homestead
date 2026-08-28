import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { ConsumableDetailPageComponent } from './consumable-detail-page.component';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { StockMovementResponse } from '@/api/generated/models/stock-movement-response';

// The hosted edit / stock-op sheets host pickers backed by ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeConsumable(over: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 'c1',
    name: 'Wood Screws',
    unit: 'screws',
    status: 'Active',
    totalOnHand: 120,
    isLow: false,
    restockThreshold: 20,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...over,
  };
}

function makeDetail(over: Partial<ConsumableDetailResponse> = {}): ConsumableDetailResponse {
  return { consumable: makeConsumable(), locations: [], ...over };
}

class StubConsumableApi {
  detail: ConsumableDetailResponse = makeDetail();
  getConsumableError = false;
  getConsumableErrorStatus = 404; // 404 → notFound; anything else → retryable loadError
  getConsumableCalls = 0;
  patchCalls: { id: string; body: unknown }[] = [];
  deleteCalls: string[] = [];
  deleteFailure: unknown = null;

  getConsumable = (_p: { id: string }) => {
    this.getConsumableCalls++;
    return this.getConsumableError
      ? throwError(() => new HttpErrorResponse({ status: this.getConsumableErrorStatus }))
      : of(this.detail);
  };
  getConsumableMovements = (p: { id: string; Page: number; PageSize: number }) =>
    of({ items: [] as StockMovementResponse[], page: p.Page, pageSize: p.PageSize, totalCount: 0, totalPages: 0 });
  patchConsumable = (p: { id: string; body: unknown }) => {
    this.patchCalls.push(p);
    return of(this.detail.consumable);
  };
  deleteConsumable = (p: { id: string }) => {
    this.deleteCalls.push(p.id);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

describe('ConsumableDetailPageComponent', () => {
  let consumableApi: StubConsumableApi;

  beforeEach(() => {
    consumableApi = new StubConsumableApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'inventory/items/:id', component: ConsumableDetailPageComponent, data: { title: 'Item' } },
        ]),
        { provide: InventoryConsumableApiService, useValue: consumableApi },
        // The hosted collapse-durables dialog injects this, but only queries it while open (it's closed here).
        { provide: InventoryDurableApiService, useValue: {} },
      ],
    });
  });

  async function navigate(url: string): Promise<{
    harness: RouterTestingHarness;
    component: ConsumableDetailPageComponent;
  }> {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, ConsumableDetailPageComponent);
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
    ).map(a => a.getAttribute('href') ?? '');
  }

  it('leads with the on-hand headline and a combined restock/Updated meta line, with no in-body back link', async () => {
    const { harness } = await navigate('/inventory/items/c1');

    const content = text(harness);
    expect(content).toContain('Wood Screws');
    expect(content).toContain('120'); // on-hand headline metric
    expect(content).toContain('Restock at or below 20 screws'); // folded into the meta line
    expect(content).toContain('Updated'); // meta line now carries the relative age
    // The shared `Items › name` breadcrumb (dispatcher) replaces the old in-body "All items" link.
    expect(content).not.toContain('All items');
    expect(hrefs(harness)).not.toContain('/inventory/items');
  });

  function buttons(harness: RouterTestingHarness): HTMLButtonElement[] {
    return Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'));
  }

  it('shows every segment of a deep stock-entry path in the By location table (no collapse)', async () => {
    consumableApi.detail = makeDetail({
      locations: [{
        id: 'e1',
        quantity: 12,
        areaId: 'house',
        locationLabel: 'House › Basement › Chest › Bin',
        path: [
          { id: 'house', name: 'House', kind: 'Area' },
          { id: 'basement', name: 'Basement', kind: 'Area' },
          { id: 'chest', name: 'Chest', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      }],
    });
    const { harness } = await navigate('/inventory/items/c1');

    const path = (harness.routeNativeElement as HTMLElement).querySelector('app-location-path')!;
    // A full-width surface: the middle is shown, not collapsed into an ellipsis.
    expect(path.textContent!.replace(/\u00a0/g, ' ')).toBe('House › Basement › Chest › Bin');
    expect(path.textContent).not.toContain('…');
  });

  it('shows the not-found state on a 404', async () => {
    consumableApi.getConsumableError = true;
    consumableApi.getConsumableErrorStatus = 404;
    const { harness, component } = await navigate('/inventory/items/missing');

    expect(component.notFound()).toBe(true);
    expect(component.loadError()).toBe(false);
    expect(text(harness)).toContain('Consumable not found');
  });

  it('shows a retryable error state (not "not found") on a non-404 load failure, and Retry re-fetches', async () => {
    consumableApi.getConsumableError = true;
    consumableApi.getConsumableErrorStatus = 500;
    const { harness, component } = await navigate('/inventory/items/c1');

    expect(component.notFound()).toBe(false);
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't load consumable");
    expect(text(harness)).not.toContain('Consumable not found');

    consumableApi.getConsumableError = false;
    buttons(harness).find(b => b.textContent?.trim() === 'Retry')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(consumableApi.getConsumableCalls).toBe(2);
    expect(text(harness)).toContain('Wood Screws');
    expect(component.loadError()).toBe(false);
  });

  it('keeps the rendered page and shows a banner when a refetch fails (non-404)', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    expect(text(harness)).toContain('Wood Screws');

    consumableApi.getConsumableError = true;
    consumableApi.getConsumableErrorStatus = 500;
    component.onStockUpdated(); // a mutation bumps refreshTrigger → refetch fails
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(component.consumable()).not.toBeNull();
    expect(text(harness)).toContain('Wood Screws'); // last-loaded page preserved
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't refresh");
    expect(text(harness)).not.toContain('may have been deleted');
  });

  it('archives an active consumable: patches status → Archived and refreshes the detail', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    expect(consumableApi.getConsumableCalls).toBe(1);

    // The refresh fired after the patch returns the now-archived record.
    consumableApi.detail = makeDetail({ consumable: makeConsumable({ status: 'Archived' }) });
    component.onArchiveRestore(component.consumable()!);
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(consumableApi.patchCalls).toEqual([
      { id: 'c1', body: [{ op: 'replace', path: '/status', value: 'Archived' }] },
    ]);
    expect(consumableApi.getConsumableCalls).toBe(2); // the success handler re-fetched
    expect(component.consumable()?.status).toBe('Archived');
    expect(text(harness)).toContain('Archived'); // status badge reflects the new state
  });

  it('restores a terminal consumable: patches status → Active', async () => {
    consumableApi.detail = makeDetail({ consumable: makeConsumable({ status: 'Archived' }) });
    const { harness, component } = await navigate('/inventory/items/c1');
    expect(component.consumable()?.status).toBe('Archived');

    consumableApi.detail = makeDetail({ consumable: makeConsumable({ status: 'Active' }) });
    component.onArchiveRestore(component.consumable()!);
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(consumableApi.patchCalls).toEqual([
      { id: 'c1', body: [{ op: 'replace', path: '/status', value: 'Active' }] },
    ]);
    expect(component.consumable()?.status).toBe('Active');
  });

  it('shows a "from {source}" token in the movement meta line when a receive recorded one', async () => {
    const movement: StockMovementResponse = {
      id: 'm1',
      kind: 'Flow',
      quantity: 4,
      toAreaId: 'garage',
      toLabel: 'Garage',
      note: 'weekly run',
      source: 'Costco',
      occurredAt: '2026-06-01T00:00:00Z',
      createdAt: '2026-06-01T00:00:00Z',
    };
    consumableApi.getConsumableMovements = (p: { id: string; Page: number; PageSize: number }) =>
      of({ items: [movement], page: p.Page, pageSize: p.PageSize, totalCount: 1, totalPages: 1 });
    const { harness } = await navigate('/inventory/items/c1');

    expect(text(harness)).toContain('weekly run · from Costco');
  });

  it('renders the movement meta line without a source token when none was recorded', async () => {
    const movement: StockMovementResponse = {
      id: 'm2',
      kind: 'Flow',
      quantity: 4,
      toAreaId: 'garage',
      toLabel: 'Garage',
      note: 'weekly run',
      occurredAt: '2026-06-01T00:00:00Z',
      createdAt: '2026-06-01T00:00:00Z',
    };
    consumableApi.getConsumableMovements = (p: { id: string; Page: number; PageSize: number }) =>
      of({ items: [movement], page: p.Page, pageSize: p.PageSize, totalCount: 1, totalPages: 1 });
    const { harness } = await navigate('/inventory/items/c1');

    expect(text(harness)).toContain('weekly run');
    expect(text(harness)).not.toContain('from Costco');
  });

  it('deletes a consumable and navigates back to the item list', async () => {
    const { component } = await navigate('/inventory/items/c1');
    const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.onDeleteRequest();
    expect(component.deleteState()).toBe('open');
    let closed = false;
    component.confirmDelete({ close: () => { closed = true; } });

    expect(consumableApi.deleteCalls).toEqual(['c1']);
    expect(closed).toBe(true);
    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items']);
    expect(component.deleteError()).toBe('');
  });

  it('shows the rejection reason inline and keeps the consumable when a delete is blocked', async () => {
    const { component } = await navigate('/inventory/items/c1');
    consumableApi.deleteFailure = new HttpErrorResponse({
      status: 400,
      error: { detail: 'This consumable has stock history — archive it instead.' },
    });
    const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    component.onDeleteRequest();
    expect(component.deleteError()).toBe('');
    let closed = false;
    component.confirmDelete({ close: () => { closed = true; } });

    // Stays open so the reason is readable; not navigated away; reason surfaced inline (not toasted).
    expect(closed).toBe(false);
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(component.deleteError()).toBe('This consumable has stock history — archive it instead.');
    expect(component.deleting()).toBe(false);
  });
});
