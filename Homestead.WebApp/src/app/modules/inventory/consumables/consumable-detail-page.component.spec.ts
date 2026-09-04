import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { toast } from '@spartan-ng/brain/sonner';
import { ConsumableDetailPageComponent } from './consumable-detail-page.component';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
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
  patchFailure: HttpErrorResponse | null = null;
  patchConsumable = (p: { id: string; body: unknown }) => {
    this.patchCalls.push(p);
    if (this.patchFailure) return throwError(() => this.patchFailure);
    return of(this.detail.consumable);
  };
  deleteConsumable = (p: { id: string }) => {
    this.deleteCalls.push(p.id);
    return this.deleteFailure ? throwError(() => this.deleteFailure) : of(undefined);
  };
}

// The hosted `app-item-activity-feed` reads through InventoryItemApiService.
class StubItemApi {
  rows: GetItemActivityResponse[] = [];
  activityCalls: { id: string; Page: number; PageSize: number }[] = [];
  getItemActivity = (p: { id: string; Page: number; PageSize: number }) => {
    this.activityCalls.push(p);
    return of({ items: this.rows, page: p.Page, pageSize: p.PageSize, totalCount: this.rows.length, totalPages: 1 });
  };
  createItemNote = () => of({});
  updateItemNote = () => of({});
  deleteItemNote = () => of(undefined);
}

describe('ConsumableDetailPageComponent', () => {
  let consumableApi: StubConsumableApi;
  let itemApi: StubItemApi;

  beforeEach(() => {
    consumableApi = new StubConsumableApi();
    itemApi = new StubItemApi();
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
        { provide: InventoryItemApiService, useValue: itemApi },
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

  it('renders the description as a labelled block in the header', async () => {
    // The standing free-text field (`notes` on the wire) is labelled Description everywhere.
    consumableApi.detail = makeDetail({ consumable: makeConsumable({ notes: 'Store cool' }) });
    const { harness } = await navigate('/inventory/items/c1');

    const content = text(harness);
    expect(content).toContain('Description');
    expect(content).toContain('Store cool');
    // Scoped to the description block: the Activity feed's All | Notes toggle legitimately says
    // "Notes" further down the page, and must not fail the relabel assertion.
    const description = Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll('p'))
      .find(el => el.textContent?.trim() === 'Description')!.parentElement!;
    expect(description.textContent).toContain('Store cool');
    expect(description.textContent).not.toContain('Notes');
  });

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

  it('archives an active consumable only after confirmation: patches status → Archived and refreshes the detail', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    expect(consumableApi.getConsumableCalls).toBe(1);

    component.onArchiveRestore(component.consumable()!);
    expect(component.archiveState()).toBe('open');
    expect(consumableApi.patchCalls).toEqual([]); // nothing until the user confirms

    // The refresh fired after the patch returns the now-archived record.
    consumableApi.detail = makeDetail({ consumable: makeConsumable({ status: 'Archived' }) });
    let closed = false;
    component.confirmArchive({ close: () => { closed = true; } });
    expect(closed).toBe(true);
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

  it('keeps the archive dialog open with the rejection reason inline when the archive is blocked', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    consumableApi.patchFailure = new HttpErrorResponse({ status: 400, error: { detail: 'Cannot archive this consumable.' } });

    component.onArchiveRestore(component.consumable()!);
    let closed = false;
    component.confirmArchive({ close: () => { closed = true; } });
    harness.detectChanges();

    expect(closed).toBe(false);
    expect(component.archiveError()).toBe('Cannot archive this consumable.');
    expect(component.archiving()).toBe(false);
    expect(consumableApi.getConsumableCalls).toBe(1); // no refresh on failure

    // Re-requesting starts with a clean slate.
    consumableApi.patchFailure = null;
    component.onArchiveRestore(component.consumable()!);
    expect(component.archiveError()).toBe('');
  });

  it('toasts a restore failure on 4xx only (5xx is the interceptor\'s), and clears the busy flag', async () => {
    const toastError = vi.spyOn(toast, 'error').mockImplementation(() => '' as never);
    consumableApi.detail = makeDetail({ consumable: makeConsumable({ status: 'Archived' }) });
    const { component } = await navigate('/inventory/items/c1');

    consumableApi.patchFailure = new HttpErrorResponse({ status: 409, error: { detail: 'Consumable was modified elsewhere.' } });
    component.onArchiveRestore(component.consumable()!);
    expect(toastError).toHaveBeenCalledWith('Consumable was modified elsewhere.');
    expect(component.archiving()).toBe(false);
    expect(component.archiveState()).toBe('closed');

    toastError.mockClear();
    consumableApi.patchFailure = new HttpErrorResponse({ status: 500 });
    component.onArchiveRestore(component.consumable()!);
    expect(toastError).not.toHaveBeenCalled();
    expect(component.archiving()).toBe(false);
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

  it('titles the movement ledger Movements, not History, and puts Activity last', async () => {
    // Headed Movements so the Activity section reads distinctly. Scoped to the section headings,
    // not the page text, so unrelated copy containing "History" cannot trip it.
    const { harness } = await navigate('/inventory/items/c1');

    const headings = Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll('h2'))
      .map(h => h.textContent?.trim());
    expect(headings).toEqual(['By location', 'Movements', 'Activity']);
    expect(headings).not.toContain('History');
  });

  it('paginates Movements independently of the Activity feed', async () => {
    consumableApi.getConsumableMovements = (p: { id: string; Page: number; PageSize: number }) =>
      of({ items: [] as StockMovementResponse[], page: p.Page, pageSize: p.PageSize, totalCount: 60, totalPages: 3 });
    const { harness, component } = await navigate('/inventory/items/c1');

    component.nextPage();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(component.page()).toBe(2);
    // The feed owns its own page signal; paging Movements must not re-request the activity feed.
    expect(itemApi.activityCalls).toEqual([{ id: 'c1', Page: 1, PageSize: 20 }]);
  });

  it('refreshes the hosted activity feed when a stock op bumps the page refresh tick', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    const before = itemApi.activityCalls.length;

    component.onStockUpdated();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(itemApi.activityCalls.length).toBe(before + 1);
    expect(itemApi.activityCalls.at(-1)).toMatchObject({ id: 'c1', Page: 1 });
  });

  it('names notes among the reasons a consumable cannot be hard-deleted', async () => {
    const { harness, component } = await navigate('/inventory/items/c1');
    component.onDeleteRequest();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(document.body.textContent).toContain(
      'Consumables with stock history or notes cannot be deleted',
    );
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
