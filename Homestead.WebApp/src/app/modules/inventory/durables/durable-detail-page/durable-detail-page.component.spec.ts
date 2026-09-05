import { provideZonelessChangeDetection, type WritableSignal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { toast } from '@spartan-ng/brain/sonner';
import { DurableDetailPageComponent } from './durable-detail-page.component';
import { ItemActivityFeedComponent } from '../../shared/activity/item-activity-feed.component';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryPlaceApiService } from '@/api/services/inventory-place-api.service';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import type { GetItemActivityResponse } from '@/api/generated/models/get-item-activity-response';
import type { ItemWarrantyResponse } from '@/api/generated/models/item-warranty-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

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

const ASSEMBLY_ITEM: DurableResponse = {
  id: 'pc1',
  name: 'Gaming PC',
  fungibility: 'NonFungible',
  status: 'Active',
  isContainer: false,
  hasComponents: true,
  path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
  resolvedAreaId: 'garage',
  areaId: 'garage',
  areaName: 'Garage',
  createdAt: '2025-01-01T00:00:00Z',
  updatedAt: '2025-06-01T00:00:00Z',
};

const PC_CONTENTS: PlaceContentsResponse = {
  path: [
    { id: 'garage', name: 'Garage', kind: 'Area' },
    { id: 'pc1', name: 'Gaming PC', kind: 'Container' },
  ],
  containerItem: ASSEMBLY_ITEM,
  scopedLowStockCount: 0,
  scopedLentCount: 0,
  childAreas: [],
  containers: [],
  items: [{
    id: 'cpu', name: 'Ryzen 9', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, parentItemId: 'pc1', parentIsContainer: false, compartment: 'CPU',
    path: [], createdAt: '', updatedAt: '2025-05-01T00:00:00Z',
  }],
  consumables: [],
};

class StubDurableApi {
  item: DurableResponse = LEAF_ITEM;
  getDurableError = false;
  getDurableErrorStatus = 404; // 404 → notFound; anything else → retryable loadError
  getDurableCalls = 0;
  patchCalls: { id: string; body: unknown }[] = [];
  deleteCalls: string[] = [];

  getDurable = (params: { id: string }) => {
    this.getDurableCalls++;
    return this.getDurableError
      ? throwError(() => new HttpErrorResponse({ status: this.getDurableErrorStatus }))
      : of(this.item);
  };
  // Used by the hosted quick panel's contents-count probe (containers only).
  getDurables = () => of({ items: [], page: 1, pageSize: 1, totalCount: 0 });
  patchFailure: HttpErrorResponse | null = null;
  patchDurable = (params: { id: string; body: unknown }) => {
    this.patchCalls.push(params);
    if (this.patchFailure) return throwError(() => this.patchFailure);
    return of(this.item);
  };
  deleteDurable = (params: { id: string }) => {
    this.deleteCalls.push(params.id);
    return of(undefined);
  };
}

// The hosted `app-item-activity-feed` reads through InventoryItemApiService.
class StubItemApi {
  rows: GetItemActivityResponse[] = [];
  activityCalls: { id: string; Page: number; PageSize: number }[] = [];
  activityFails = false;
  getItemActivity = (p: { id: string; Page: number; PageSize: number }) => {
    this.activityCalls.push(p);
    if (this.activityFails) return throwError(() => new Error('boom'));
    return of({ items: this.rows, page: p.Page, pageSize: p.PageSize, totalCount: this.rows.length, totalPages: 1 });
  };
  createItemNote = () => of({});
  updateItemNote = () => of({});
  deleteItemNote = () => of(undefined);
  // The hosted `app-item-warranties-section` reads through the same service.
  warranties: ItemWarrantyResponse[] = [];
  warrantyCalls: { id: string }[] = [];
  getItemWarranties = (p: { id: string }) => {
    this.warrantyCalls.push(p);
    return of(this.warranties);
  };
  createItemWarranty = () => of({});
  updateItemWarranty = () => of({});
  deleteItemWarranty = () => of(undefined);
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
  let itemApi: StubItemApi;

  beforeEach(() => {
    durableApi = new StubDurableApi();
    placeApi = new StubPlaceApi();
    itemApi = new StubItemApi();
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
        { provide: InventoryItemApiService, useValue: itemApi },
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

  /** The activity pager now lives inside the hosted feed, so reach it through the child component. */
  function feedPage(harness: RouterTestingHarness): WritableSignal<number> {
    const feed = harness.fixture.debugElement.query(By.directive(ItemActivityFeedComponent))
      .componentInstance as unknown as { page: WritableSignal<number> };
    return feed.page;
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

  it('renders the composed Acquired line with kind, date, cost, and source', async () => {
    durableApi.item = {
      ...LEAF_ITEM,
      acquisitionKind: 'Purchased',
      acquiredOn: '2024-06-12',
      acquisitionCost: 189,
      acquisitionCostCurrencySymbol: 'CAD',
      acquiredFrom: 'Home Depot',
    };
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('Acquired');
    expect(content).toContain('Purchased · Jun 12, 2024 · 189 CAD · from Home Depot');
    // Value relabelled now that a purchase price can sit beside it.
    expect(content).toContain('Est. value');
    expect(content).not.toContain('Acquired on');
  });

  it('renders a date-only Acquired line like today when only acquiredOn is set', async () => {
    durableApi.item = { ...LEAF_ITEM, acquiredOn: '2024-06-12' };
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('Acquired');
    expect(content).toContain('Jun 12, 2024');
    expect(content).not.toContain('·'); // a single part renders without separators
  });

  it('shows the Details box when only an acquisition field is set', async () => {
    durableApi.item = {
      id: 'bare2', name: 'Gifted Thing', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [], createdAt: '', updatedAt: '2025-06-01T00:00:00Z',
      acquisitionKind: 'Gift',
    };
    const { harness } = await navigate('/inventory/items/bare2');

    const content = text(harness);
    expect(content).toContain('Details');
    expect(content).toContain('Gift');
  });

  it('shows the Details box with a Description row when only a description is set', async () => {
    // The standing free-text field (`notes` on the wire) is labelled Description and lives in the
    // Details box, so a description alone must bring the box out — not leave it hidden.
    durableApi.item = {
      id: 'bare3', name: 'Described Thing', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [], createdAt: '', updatedAt: '2025-06-01T00:00:00Z',
      notes: 'Keep dry',
    };
    const { harness } = await navigate('/inventory/items/bare3');

    expect(text(harness)).toContain('Details');
    // Scope to the Details list (the page's only <dl>) rather than the whole route: the upcoming
    // Activity feed puts an "All | Notes" toggle on this page, which must not fail this test.
    const details = (harness.routeNativeElement as HTMLElement).querySelector('dl')!;
    expect(details).not.toBeNull();
    const rows = details.textContent ?? '';
    expect(rows).toContain('Description');
    expect(rows).toContain('Keep dry');
    expect(rows).not.toContain('Notes');
  });

  it('shows Unplaced as the location headline for an unplaced durable', async () => {
    durableApi.item = {
      id: 'floating', name: 'Floating Thing', fungibility: 'NonFungible', status: 'Active',
      isContainer: false, path: [], createdAt: '', updatedAt: '2025-06-01T00:00:00Z',
    };
    const { harness } = await navigate('/inventory/items/floating');

    expect(text(harness)).toContain('Unplaced');
  });

  it('shows the not-found state on a 404', async () => {
    durableApi.getDurableError = true;
    durableApi.getDurableErrorStatus = 404;
    const { harness, component } = await navigate('/inventory/items/missing');

    expect(component.notFound()).toBe(true);
    expect(component.loadError()).toBe(false);
    expect(text(harness)).toContain('Durable not found');
  });

  it('shows a retryable error state (not "not found") on a non-404 load failure', async () => {
    durableApi.getDurableError = true;
    durableApi.getDurableErrorStatus = 500;
    const { harness, component } = await navigate('/inventory/items/item1');

    expect(component.notFound()).toBe(false);
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't load durable");
    expect(text(harness)).not.toContain('Durable not found');

    // Retry re-runs the fetch; with the backend recovered the page renders.
    durableApi.getDurableError = false;
    buttons(harness).find(b => b.textContent?.trim() === 'Retry')!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(durableApi.getDurableCalls).toBe(2);
    expect(text(harness)).toContain('Cordless Drill');
    expect(component.loadError()).toBe(false);
  });

  it('keeps the rendered page and shows a banner when a refetch fails (non-404)', async () => {
    const { harness, component } = await navigate('/inventory/items/item1');
    expect(text(harness)).toContain('Cordless Drill');

    // A mutation-triggered refetch fails transiently — the page must stay rendered.
    durableApi.getDurableError = true;
    durableApi.getDurableErrorStatus = 500;
    component.onChanged();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(component.durable()).not.toBeNull();
    expect(text(harness)).toContain('Cordless Drill'); // last-loaded page preserved
    expect(component.loadError()).toBe(true);
    expect(text(harness)).toContain("Couldn't refresh");
    expect(text(harness)).not.toContain('may have been deleted');
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

  it('renders a Components section (with slot label) for an assembly, not a Contents section', async () => {
    durableApi.item = ASSEMBLY_ITEM;
    placeApi.contents = PC_CONTENTS;
    const { harness } = await navigate('/inventory/items/pc1');

    expect(placeApi.calls).toEqual([{ ContainerItemId: 'pc1' }]);
    const content = text(harness);
    expect(content).toContain('Components');
    expect(content).not.toContain('Contents');
    expect(content).toContain('Ryzen 9'); // the component
    expect(content).toContain('CPU'); // its compartment / slot label
    expect(content).toContain('Add part');
    // An assembly already has components, so it can't be flipped to a container.
    expect(content).not.toContain('Store items inside');
  });

  it('pre-parents the create dialog under the item for "Add part"', async () => {
    durableApi.item = ASSEMBLY_ITEM;
    placeApi.contents = PC_CONTENTS;
    const { component } = await navigate('/inventory/items/pc1');

    expect(component['addComponentLocation']()).toEqual({ kind: 'container', id: 'pc1' });
  });

  it('offers "Store items inside" on an empty regular item and PATCHes /isContainer', async () => {
    // A regular item with no components renders a static Components section — no contents fetch — plus
    // the "Store items inside" intent action that converts it to a container.
    durableApi.item = LEAF_ITEM;
    const { harness, component } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('Components');
    expect(content).toContain('Store items inside');
    expect(content).toContain('Add part');
    expect(placeApi.calls).toEqual([]); // empty regular item never fetches

    component.onStoreItemsInside(LEAF_ITEM);
    expect(durableApi.patchCalls).toEqual([
      { id: 'item1', body: [{ op: 'replace', path: '/isContainer', value: true }] },
    ]);
  });

  it('archives only after confirmation, and restores immediately', async () => {
    const { component } = await navigate('/inventory/items/item1');
    const durable = component.durable()!;

    component.onArchiveRestore(durable);
    expect(component.archiveState()).toBe('open');
    expect(durableApi.patchCalls).toEqual([]); // nothing until the user confirms

    let closed = false;
    component.confirmArchive({ close: () => { closed = true; component.archiveState.set('closed'); } });
    expect(closed).toBe(true);
    expect(durableApi.patchCalls).toEqual([
      {
        id: 'item1',
        body: [
          { op: 'replace', path: '/status', value: 'Archived' },
          { op: 'replace', path: '/statusNote', value: null },
        ],
      },
    ]);

    component.onArchiveRestore({ ...durable, status: 'Archived' });
    expect(component.archiveState()).toBe('closed');
    expect(durableApi.patchCalls[1]).toEqual(
      { id: 'item1', body: [{ op: 'replace', path: '/status', value: 'Active' }] },
    );
  });

  it('sends the typed archive note, trimmed, and resets it on the next open', async () => {
    const { component } = await navigate('/inventory/items/item1');

    component.onArchiveRestore(component.durable()!);
    component.archiveNote.set('  Sold to Sam  ');
    component.confirmArchive({ close: () => {} });

    expect(durableApi.patchCalls).toEqual([
      {
        id: 'item1',
        body: [
          { op: 'replace', path: '/status', value: 'Archived' },
          { op: 'replace', path: '/statusNote', value: 'Sold to Sam' },
        ],
      },
    ]);

    // Reopening starts from an empty note rather than the previous entry's.
    component.onArchiveRestore(component.durable()!);
    expect(component.archiveNote()).toBe('');
  });

  it('lends only after the dialog collects a borrower, and marks returned immediately', async () => {
    const { component } = await navigate('/inventory/items/item1');
    const durable = component.durable()!;

    component.onLendRequest();
    expect(component.lendState()).toBe('open');
    expect(durableApi.patchCalls).toEqual([]); // nothing until a borrower is typed

    // A blank borrower is refused — the action button is disabled for the same reason.
    component.confirmLend({ close: () => {} });
    expect(durableApi.patchCalls).toEqual([]);

    let closed = false;
    component.lendNote.set('  Dave next door  ');
    component.confirmLend({ close: () => { closed = true; } });
    expect(closed).toBe(true);
    expect(durableApi.patchCalls).toEqual([
      {
        id: 'item1',
        body: [
          { op: 'replace', path: '/status', value: 'Lent' },
          { op: 'replace', path: '/statusNote', value: 'Dave next door' },
        ],
      },
    ]);

    // Returning has no dialog — going back to Active clears the note server-side.
    component.onMarkReturned({ ...durable, status: 'Lent', statusNote: 'Dave next door' });
    expect(durableApi.patchCalls[1]).toEqual(
      { id: 'item1', body: [{ op: 'replace', path: '/status', value: 'Active' }] },
    );
  });

  it('keeps the lend dialog open with the rejection reason inline when the lend is refused', async () => {
    const { component } = await navigate('/inventory/items/item1');
    durableApi.patchFailure = new HttpErrorResponse({
      status: 400, error: { detail: 'StatusNote is required when the status is Lent.' },
    });

    component.onLendRequest();
    component.lendNote.set('Dave');
    let closed = false;
    component.confirmLend({ close: () => { closed = true; } });

    expect(closed).toBe(false);
    // The lend dialog owns its own error slot — the archive dialog's must stay untouched.
    expect(component.lendError()).toBe('StatusNote is required when the status is Lent.');
    expect(component.archiveError()).toBe('');
    expect(component.archiving()).toBe(false);
    expect(durableApi.getDurableCalls).toBe(1); // no refresh on failure

    // Re-requesting starts with a clean slate.
    durableApi.patchFailure = null;
    component.onLendRequest();
    expect(component.lendError()).toBe('');
    expect(component.lendNote()).toBe('');
  });

  it('keeps the archive dialog open with the rejection reason inline when the archive is blocked', async () => {
    const { component } = await navigate('/inventory/items/item1');
    durableApi.patchFailure = new HttpErrorResponse({
      status: 400, error: { detail: 'Cannot archive a container while it has live contents.' },
    });

    component.onArchiveRestore(component.durable()!);
    let closed = false;
    component.confirmArchive({ close: () => { closed = true; } });

    expect(closed).toBe(false);
    expect(component.archiveError()).toBe('Cannot archive a container while it has live contents.');
    expect(component.archiving()).toBe(false);
    expect(durableApi.getDurableCalls).toBe(1); // no refresh on failure

    // Re-requesting starts with a clean slate.
    durableApi.patchFailure = null;
    component.onArchiveRestore(component.durable()!);
    expect(component.archiveError()).toBe('');
  });

  it('toasts a restore failure on 4xx only (5xx is the interceptor\'s), and clears the busy flag', async () => {
    const toastError = vi.spyOn(toast, 'error').mockImplementation(() => '' as never);
    const { component } = await navigate('/inventory/items/item1');
    const archived = { ...component.durable()!, status: 'Archived' as const };

    durableApi.patchFailure = new HttpErrorResponse({ status: 409, error: { detail: 'Durable was modified elsewhere.' } });
    component.onArchiveRestore(archived);
    expect(toastError).toHaveBeenCalledWith('Durable was modified elsewhere.');
    expect(component.archiving()).toBe(false);
    expect(component.archiveState()).toBe('closed');

    toastError.mockClear();
    durableApi.patchFailure = new HttpErrorResponse({ status: 500 });
    component.onArchiveRestore(archived);
    expect(toastError).not.toHaveBeenCalled();
    expect(component.archiving()).toBe(false);
  });

  it('renders the shared Activity feed (not the old History section) with each summary', async () => {
    itemApi.rows = [
      { id: 'a1', type: 'audit_log', summary: 'Alice updated value, location', timestamp: '2025-06-01T00:00:00Z', userDisplayName: 'Alice', userEmail: null },
      { id: 'a2', type: 'audit_log', summary: 'Someone added this item', timestamp: '2025-05-01T00:00:00Z', userDisplayName: null, userEmail: null },
    ];
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain('Activity');
    expect(content).not.toContain('History');
    expect(content).toContain('Alice updated value, location');
    // A null/deleted actor still renders (summary says "Someone"), never vanishes.
    expect(content).toContain('Someone added this item');
  });

  it('hosts the Warranties section between Components/Contents and Activity', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.contents = CHEST_CONTENTS;
    itemApi.warranties = [{
      id: 'w1', itemId: 'box1', provider: 'Home Depot', expiresOn: '2028-03-12', notes: null,
      status: 'Active', createdAt: '2026-06-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z',
    }];
    const { harness } = await navigate('/inventory/items/box1');

    expect(itemApi.warrantyCalls).toEqual([{ id: 'box1' }]);
    const content = text(harness);
    expect(content).toContain('Warranties');
    expect(content).toContain('Home Depot');
    // Section order: Details → Contents → Warranties → Activity.
    const headings = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('h2'),
    ).map(h => h.textContent?.trim());
    expect(headings.indexOf('Contents')).toBeLessThan(headings.indexOf('Warranties'));
    expect(headings.indexOf('Warranties')).toBeLessThan(headings.indexOf('Activity'));
  });

  it('resets the activity page and open overlays when navigating to another item (component reused)', async () => {
    durableApi.item = CONTAINER_ITEM;
    placeApi.contents = CHEST_CONTENTS;
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/inventory/items/box1', DurableDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();

    // Simulate user state: paged the activity feed forward and opened the edit sheet + quick panel.
    feedPage(harness).set(2);
    component.editState.set('open');
    component.durableQuickPanelState.set('open');
    harness.detectChanges();
    await harness.fixture.whenStable();
    expect(itemApi.activityCalls.at(-1)).toMatchObject({ id: 'box1', Page: 2 });

    // Navigate to a different item via the same route (Angular reuses the component instance).
    durableApi.item = LEAF_ITEM;
    const component2 = await harness.navigateByUrl('/inventory/items/item1', DurableDetailPageComponent);
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(component2).toBe(component); // confirms the reuse the fix guards against
    // The feed's page resets to 1, so the new item's activity is fetched at page 1 — not the stale page 2.
    expect(feedPage(harness)()).toBe(1);
    expect(itemApi.activityCalls.at(-1)).toMatchObject({ id: 'item1', Page: 1 });
    // overlays opened for the previous item are closed, not left hanging over the new one.
    expect(component2.editState()).toBe('closed');
    expect(component2.durableQuickPanelState()).toBe('closed');
  });

  it('shows an error state (not a false empty) when the activity fetch fails', async () => {
    itemApi.activityFails = true;
    const { harness } = await navigate('/inventory/items/item1');

    const content = text(harness);
    expect(content).toContain("Couldn't load activity.");
    expect(content).not.toContain('No activity yet.');
  });

  it('refreshes the hosted activity feed when a mutation bumps the page refresh tick', async () => {
    const { harness, component } = await navigate('/inventory/items/item1');
    const before = itemApi.activityCalls.length;

    component.onChanged();
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(itemApi.activityCalls.length).toBe(before + 1);
    expect(itemApi.activityCalls.at(-1)).toMatchObject({ id: 'item1', Page: 1 });
  });

  it('names notes among the reasons a durable cannot be hard-deleted', async () => {
    const { harness, component } = await navigate('/inventory/items/item1');
    component.onDeleteRequest();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(document.body.textContent).toContain(
      'Durables with contents, stock, or notes cannot be deleted',
    );
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
