import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { toast } from '@spartan-ng/brain/sonner';
import { InventoryItemsViewComponent } from './inventory-items-view.component';
import { InventoryTableComponent } from './inventory-table.component';
import { InventoryCardGridComponent } from './inventory-card-grid.component';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { InventoryDurableApiService } from '@/api/services/inventory-durable-api.service';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { durableToRow, consumableToRow, type InventoryRow } from './inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { stubMatchMedia } from '@/shared/testing/match-media';

// brn-select (in the edit sheets) uses ResizeObserver; safe to stub for the rendered template.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(over: Partial<DurableResponse>): DurableResponse {
  return {
    id: 'x', name: 'Item', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '', ...over,
  };
}

function makeConsumable(over: Partial<ConsumableResponse>): ConsumableResponse {
  return {
    id: 's', name: 'Consumable', status: 'Active', unit: 'pcs', totalOnHand: 0,
    isLow: false, createdAt: '', updatedAt: '', stockLocations: [], ...over,
  };
}

const WRENCH = makeItem({ id: 'wrench', name: 'Wrench' });
const LADDER = makeItem({ id: 'ladder', name: 'Ladder', status: 'Lent' as ItemStatus, statusNote: 'Dave' });
const ARCHIVED = makeItem({ id: 'archived', name: 'Old rag', status: 'Archived' as ItemStatus });
const SCREWS = makeConsumable({ id: 'screws', name: 'Wood screws', totalOnHand: 10, isLow: true });
const TAPE = makeConsumable({ id: 'tape', name: 'Tape', totalOnHand: 5 });
const OLDGLUE = makeConsumable({ id: 'oldglue', name: 'Old glue', status: 'Archived' as ItemStatus, totalOnHand: 1, isLow: true });

const ROWS: InventoryRow[] = [
  durableToRow(WRENCH), durableToRow(LADDER), durableToRow(ARCHIVED),
  consumableToRow(SCREWS), consumableToRow(TAPE), consumableToRow(OLDGLUE),
];

class StubDurableApi {
  patchDurable = vi.fn(() => of(makeItem({})));
  deleteDurable = vi.fn(() => of(undefined));
}
class StubConsumableApi {
  patchConsumable = vi.fn(() => of(makeConsumable({})));
  deleteConsumable = vi.fn(() => of(undefined));
}

describe('InventoryItemsViewComponent', () => {
  let fixture: ComponentFixture<InventoryItemsViewComponent>;
  let component: InventoryItemsViewComponent;
  let durableApi: StubDurableApi;
  let consumableApi: StubConsumableApi;
  let mockStorage: Record<string, string>;

  beforeEach(async () => {
    durableApi = new StubDurableApi();
    consumableApi = new StubConsumableApi();
    // jsdom has no usable localStorage, and the view-mode pref is a persistedSignal. A fresh
    // in-memory record per test means no pref leaks into the next one.
    mockStorage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: InventoryDurableApiService, useValue: durableApi },
        { provide: InventoryConsumableApiService, useValue: consumableApi },
      ],
    });
    fixture = TestBed.createComponent(InventoryItemsViewComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('rows', ROWS);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => vi.unstubAllGlobals());

  function setViewPref(mode: 'table' | 'card' | null): void {
    TestBed.inject(UserPreferencesService).inventoryItemViewMode.set(mode);
  }

  function ids(rows: readonly InventoryRow[]): string[] {
    return rows.map(r => r.id).sort();
  }

  it('hides terminal rows by default and surfaces them when Show inactive is on', () => {
    expect(ids(component.visibleRows())).toEqual(['ladder', 'screws', 'tape', 'wrench']);
    expect(component.inactiveCount()).toBe(2);

    component.showInactive.set(true);
    expect(ids(component.visibleRows())).toEqual(['archived', 'ladder', 'oldglue', 'screws', 'tape', 'wrench']);
  });

  it('kind filter narrows to durables or consumables', () => {
    component.onKindFilterChange('durables');
    expect(ids(component['kindRows']())).toEqual(['archived', 'ladder', 'wrench']);

    component.onKindFilterChange('consumables');
    expect(ids(component['kindRows']())).toEqual(['oldglue', 'screws', 'tape']);

    component.onKindFilterChange('all');
    expect(component['kindRows']().length).toBe(ROWS.length);
  });

  it('low-stock filter keeps only low consumables, counting archived lows only when inactive shows', () => {
    component.lowStockOnly.set(true);
    expect(ids(component.visibleRows())).toEqual(['screws']);
    expect(component.lowCount()).toBe(1);

    component.showInactive.set(true);
    expect(component.lowCount()).toBe(2);
    expect(ids(component.visibleRows())).toEqual(['oldglue', 'screws']);
  });

  it('lent-only filter keeps only Lent rows', () => {
    component.lentOnly.set(true);
    expect(ids(component.visibleRows())).toEqual(['ladder']);
    expect(component.lentCount()).toBe(1);
  });

  it('sorts the visible rows by the chosen column/direction', () => {
    component.sort.set({ id: 'name', desc: true });
    expect(component.sortedVisibleRows().map(r => r.id)).toEqual(['wrench', 'screws', 'tape', 'ladder']);
  });

  it('computes the sort field label and the popover filter label', () => {
    expect(component.filterLabel()).toBe('Filter');
    component.lowStockOnly.set(true);
    expect(component.filterLabel()).toBe('Filter: Low stock');
    // A parent scope label wins over the component's own filters.
    fixture.componentRef.setInput('scopeLabel', 'In: Tool chest');
    expect(component.filterLabel()).toBe('In: Tool chest');

    component.onSortByChange('updatedAt');
    expect(component.sortFieldLabel()).toBe('Updated');
    expect(component.sort()).toEqual({ id: 'updatedAt', desc: true });
  });

  it('dispatches a row open to the kind-matched output (panels stay page-hosted)', () => {
    let durable: DurableResponse | undefined;
    let consumable: ConsumableResponse | undefined;
    component.openDurable.subscribe(d => (durable = d));
    component.openConsumable.subscribe(c => (consumable = c));

    component.onOpen(durableToRow(WRENCH));
    expect(durable).toBe(WRENCH);
    component.onOpen(consumableToRow(TAPE));
    expect(consumable).toBe(TAPE);
  });

  it('opens the matching edit sheet by row kind', () => {
    component.onEdit(durableToRow(WRENCH));
    expect(component.selectedDurable()).toBe(WRENCH);
    expect(component.durableSheetState()).toBe('open');
    expect(component.consumableSheetState()).toBe('closed');

    component.onEdit(consumableToRow(TAPE));
    expect(component.selectedConsumable()).toBe(TAPE);
    expect(component.consumableSheetState()).toBe('open');
  });

  it('re-emits the table viewContents up and navigates on viewDetails', () => {
    let viewed: InventoryRow | undefined;
    component.viewContents.subscribe(r => (viewed = r));
    const table = fixture.debugElement.query(By.directive(InventoryTableComponent)).componentInstance as InventoryTableComponent;
    table.viewContents.emit(durableToRow(WRENCH));
    expect(viewed?.id).toBe('wrench');

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.onViewDetails(durableToRow(WRENCH));
    expect(navigate).toHaveBeenCalledWith(['/inventory/items', 'wrench']);
  });

  it('restores a terminal row immediately and signals changed', () => {
    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.onArchiveRestore(durableToRow(ARCHIVED));
    expect(durableApi.patchDurable).toHaveBeenCalledWith({ id: 'archived', body: [{ op: 'replace', path: '/status', value: 'Active' }] });
    expect(component.archiveState()).toBe('closed');
    expect(changed).toBe(1);

    component.onArchiveRestore(consumableToRow(OLDGLUE));
    expect(consumableApi.patchConsumable).toHaveBeenCalledWith({ id: 'oldglue', body: [{ op: 'replace', path: '/status', value: 'Active' }] });
    expect(changed).toBe(2);
  });

  it('archives only after confirmation, via the matching API, and signals changed', () => {
    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.onArchiveRestore(durableToRow(WRENCH));
    expect(component.archiveState()).toBe('open');
    expect(component.pendingArchive()?.id).toBe('wrench');
    expect(durableApi.patchDurable).not.toHaveBeenCalled();
    expect(changed).toBe(0);

    // A blank note still sends the op, clearing any note the row carried.
    let closed = false;
    component.confirmArchive({ close: () => { closed = true; } });
    expect(durableApi.patchDurable).toHaveBeenCalledWith({
      id: 'wrench',
      body: [
        { op: 'replace', path: '/status', value: 'Archived' },
        { op: 'replace', path: '/statusNote', value: null },
      ],
    });
    expect(closed).toBe(true);
    expect(component.pendingArchive()).toBeNull();
    expect(changed).toBe(1);

    component.onArchiveRestore(consumableToRow(TAPE));
    component.confirmArchive({ close: () => {} });
    expect(consumableApi.patchConsumable).toHaveBeenCalledWith({
      id: 'tape',
      body: [
        { op: 'replace', path: '/status', value: 'Archived' },
        { op: 'replace', path: '/statusNote', value: null },
      ],
    });
    expect(changed).toBe(2);
  });

  it('sends the typed archive note, trimmed, and resets it on the next open', () => {
    component.onArchiveRestore(durableToRow(WRENCH));
    component.archiveNote.set('  Sold to Sam  ');
    component.confirmArchive({ close: () => {} });

    expect(durableApi.patchDurable).toHaveBeenCalledWith({
      id: 'wrench',
      body: [
        { op: 'replace', path: '/status', value: 'Archived' },
        { op: 'replace', path: '/statusNote', value: 'Sold to Sam' },
      ],
    });

    // Reopening starts from an empty note rather than the previous entry's.
    component.onArchiveRestore(durableToRow(WRENCH));
    expect(component.archiveNote()).toBe('');
  });

  it('shows the rejection reason inline and keeps the dialog open when an archive is blocked', () => {
    durableApi.patchDurable = vi.fn(() => throwError(() => new HttpErrorResponse({
      status: 400,
      error: { detail: 'Container has live contents — empty it first.' },
    })));

    component.onArchiveRestore(durableToRow(WRENCH));
    let closed = false;
    component.confirmArchive({ close: () => { closed = true; } });

    expect(closed).toBe(false);
    expect(component.archiveError()).toBe('Container has live contents — empty it first.');
    expect(component.pendingArchive()?.id).toBe('wrench');
    expect(component.archiving()).toBe(false);

    // Re-requesting an archive starts with a clean slate.
    component.onArchiveRestore(consumableToRow(TAPE));
    expect(component.archiveError()).toBe('');
  });

  it("toasts a restore failure on 4xx only (5xx is the interceptor's), and clears the busy flag", () => {
    const toastError = vi.spyOn(toast, 'error').mockImplementation(() => '' as never);
    let changed = 0;
    component.changed.subscribe(() => changed++);

    durableApi.patchDurable = vi.fn(() => throwError(() => new HttpErrorResponse({
      status: 409, error: { detail: 'Durable was modified elsewhere.' },
    })));
    component.onArchiveRestore(durableToRow(ARCHIVED));
    expect(toastError).toHaveBeenCalledWith('Durable was modified elsewhere.');
    expect(component.archiving()).toBe(false);
    expect(component.archiveState()).toBe('closed');
    expect(changed).toBe(0);

    toastError.mockClear();
    durableApi.patchDurable = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    component.onArchiveRestore(durableToRow(ARCHIVED));
    expect(toastError).not.toHaveBeenCalled();
    expect(component.archiving()).toBe(false);
  });

  it('deletes on confirm and signals changed', () => {
    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.onDeleteRequest(durableToRow(WRENCH));
    expect(component.deleteState()).toBe('open');
    let closed = false;
    component.confirmDelete({ close: () => { closed = true; } });

    expect(durableApi.deleteDurable).toHaveBeenCalled();
    expect(closed).toBe(true);
    expect(component.pendingDelete()).toBeNull();
    expect(changed).toBe(1);
  });

  it('shows the rejection reason inline and keeps the dialog open when a delete is blocked', () => {
    consumableApi.deleteConsumable = vi.fn(() => throwError(() => new HttpErrorResponse({
      status: 400,
      error: { detail: 'This consumable has stock history — archive it instead.' },
    })));

    component.onDeleteRequest(consumableToRow(SCREWS));
    expect(component.deleteError()).toBe('');
    let closed = false;
    component.confirmDelete({ close: () => { closed = true; } });

    expect(closed).toBe(false);
    expect(component.deleteError()).toBe('This consumable has stock history — archive it instead.');
    expect(component.pendingDelete()?.id).toBe('screws');
    expect(component.deleting()).toBe(false);

    // Re-requesting a delete starts with a clean slate.
    component.onDeleteRequest(consumableToRow(TAPE));
    expect(component.deleteError()).toBe('');
  });

  it('Reset clears its own refinements and emits reset for the parent scope', () => {
    component.showInactive.set(true);
    component.lowStockOnly.set(true);
    component.lentOnly.set(true);
    component.warrantyFilter.set('expiring');
    let reset = 0;
    component.reset.subscribe(() => reset++);

    component.onReset();
    expect(component.showInactive()).toBe(false);
    expect(component.lowStockOnly()).toBe(false);
    expect(component.lentOnly()).toBe(false);
    expect(component.warrantyFilter()).toBe('any');
    expect(reset).toBe(1);
  });

  describe('warranty facet', () => {
    // A dedicated row set: the shared ROWS carry no warranty status, and the facet is durables-only.
    const COVERED = makeItem({ id: 'covered', name: 'Covered', warrantyStatus: 'Active' });
    const SOON = makeItem({ id: 'soon', name: 'Soon', warrantyStatus: 'Expiring' });
    const LAPSED = makeItem({ id: 'lapsed', name: 'Lapsed', warrantyStatus: 'Expired' });
    const BARE = makeItem({ id: 'bare', name: 'Bare', warrantyStatus: 'None' });
    // An archived durable that would match a facet — the counts must hide it while the list does.
    const SHELVED = makeItem({
      id: 'shelved', name: 'Shelved', status: 'Archived' as ItemStatus, warrantyStatus: 'Expiring',
    });
    const WARRANTY_ROWS: InventoryRow[] = [
      durableToRow(COVERED), durableToRow(SOON), durableToRow(LAPSED), durableToRow(BARE),
      durableToRow(SHELVED), consumableToRow(TAPE),
    ];

    beforeEach(() => {
      fixture.componentRef.setInput('rows', WARRANTY_ROWS);
      fixture.detectChanges();
    });

    it('is off by default and keeps every row', () => {
      expect(component.warrantyFilter()).toBe('any');
      // 'shelved' is archived, so the default (Show inactive off) list omits it.
      expect(ids(component.visibleRows())).toEqual(['bare', 'covered', 'lapsed', 'soon', 'tape']);
    });

    it('keeps only the rows matching the chosen state, dropping consumables entirely', () => {
      component.warrantyFilter.set('expiring');
      expect(ids(component.visibleRows())).toEqual(['soon']);

      component.warrantyFilter.set('expired');
      expect(ids(component.visibleRows())).toEqual(['lapsed']);

      component.warrantyFilter.set('none');
      expect(ids(component.visibleRows())).toEqual(['bare']);

      component.warrantyFilter.set('active');
      expect(ids(component.visibleRows())).toEqual(['covered']);
    });

    it('counts each state over the durable rows, with Any counting everything countable', () => {
      expect(component.warrantyCounts()).toEqual({
        any: 5, active: 1, expiring: 1, expired: 1, none: 1,
      });
    });

    // The bug this guards: counts taken over the un-narrowed rows promised more than the list could
    // show ("None (44)" beside a list yielding 35). Every badge must equal what picking it yields.
    it('excludes inactive rows from the counts until Show inactive reveals them', () => {
      expect(component.warrantyCounts().expiring).toBe(1);
      expect(ids(component.visibleRows())).not.toContain('shelved');

      component.showInactive.set(true);
      expect(component.warrantyCounts().expiring).toBe(2);
      expect(component.warrantyCounts().any).toBe(6);
    });

    it('every facet badge equals the number of rows selecting it actually shows', () => {
      for (const inactive of [false, true]) {
        component.showInactive.set(inactive);
        for (const facet of ['any', 'active', 'expiring', 'expired', 'none'] as const) {
          component.warrantyFilter.set(facet);
          expect(component.visibleRows().length).toBe(component.warrantyCounts()[facet]);
        }
      }
      component.showInactive.set(false);
      component.warrantyFilter.set('any');
    });

    it('names the active facet in the popover trigger label', () => {
      component.warrantyFilter.set('expiring');
      expect(component.filterLabel()).toBe('Filter: Warranty expiring');
      component.warrantyFilter.set('none');
      expect(component.filterLabel()).toBe('Filter: Warranty none');
    });
  });

  it('renders "Nothing matches" (not a spinner) when nothing is visible and not loading', async () => {
    fixture.componentRef.setInput('rows', []);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Nothing matches');
    expect(host.querySelector('hlm-spinner')).toBeNull();

    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    expect(host.querySelector('hlm-spinner')).not.toBeNull();
  });

  describe('one-shot initial* seeding', () => {
    async function createSeeded(seed: Record<string, unknown>): Promise<InventoryItemsViewComponent> {
      const f = TestBed.createComponent(InventoryItemsViewComponent);
      f.componentRef.setInput('rows', ROWS);
      for (const [k, v] of Object.entries(seed)) f.componentRef.setInput(k, v);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();
      return f.componentInstance;
    }

    it('seeds the low-stock lens and reflects it in the visible rows', async () => {
      const c = await createSeeded({ initialLowStockOnly: true });
      expect(c.lowStockOnly()).toBe(true);
      expect(ids(c.visibleRows())).toEqual(['screws']);
    });

    it('seeds the lent lens', async () => {
      const c = await createSeeded({ initialLentOnly: true });
      expect(c.lentOnly()).toBe(true);
      expect(ids(c.visibleRows())).toEqual(['ladder']);
    });

    it('seeds the warranty facet from the Browse tile deep link', async () => {
      const c = await createSeeded({ initialWarranty: 'expiring' });
      expect(c.warrantyFilter()).toBe('expiring');
    });

    it('seeds the kind toggle and the sort state', async () => {
      const c = await createSeeded({ initialKind: 'durables', initialSort: { id: 'onHand', desc: true } });
      expect(c.kind()).toBe('durables');
      expect(c.sort()).toEqual({ id: 'onHand', desc: true });
    });
  });

  it('hides the Show inactive checkbox when showInactiveFilter is false', async () => {
    // Default (showInactiveFilter = true): open the popover and the checkbox is present.
    const trigger = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'))
      .find(b => b.textContent?.includes('Filter'))!;
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(document.body.textContent).toContain('Show inactive');

    // Browse passes false → the checkbox is gone (low/lent stay).
    fixture.componentRef.setInput('showInactiveFilter', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(document.body.textContent).not.toContain('Show inactive');
    expect(document.body.textContent).toContain('Low stock only');
  });
  describe('view mode', () => {
    it('falls back to the table on a wide viewport when the pref is unset', () => {
      expect(TestBed.inject(UserPreferencesService).inventoryItemViewMode()).toBeNull();
      expect(component.viewMode()).toBe('table');
    });

    it('honours an explicit stored preference in either direction', () => {
      setViewPref('card');
      expect(component.viewMode()).toBe('card');
      setViewPref('table');
      expect(component.viewMode()).toBe('table');
    });

    it('renders the card grid instead of the table when the pref is card', async () => {
      setViewPref('card');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('app-inventory-card-grid')).not.toBeNull();
      expect(host.querySelector('app-inventory-table')).toBeNull();
    });

    it('dispatches a card-grid open to the kind-matched output', async () => {
      setViewPref('card');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      let durable: DurableResponse | undefined;
      let consumable: ConsumableResponse | undefined;
      component.openDurable.subscribe(d => (durable = d));
      component.openConsumable.subscribe(c => (consumable = c));

      const grid = fixture.debugElement.query(By.directive(InventoryCardGridComponent))
        .componentInstance as InventoryCardGridComponent;
      grid.open.emit(durableToRow(WRENCH));
      expect(durable).toBe(WRENCH);
      grid.open.emit(consumableToRow(TAPE));
      expect(consumable).toBe(TAPE);
    });

    it('setViewMode writes the preference', () => {
      component.setViewMode('card');
      expect(TestBed.inject(UserPreferencesService).inventoryItemViewMode()).toBe('card');
      component.setViewMode('table');
      expect(TestBed.inject(UserPreferencesService).inventoryItemViewMode()).toBe('table');
    });

    it('renders no view toggle when showViewToggle is false (Browse inherits the pref)', async () => {
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('[aria-label="View mode"]')).not.toBeNull();

      fixture.componentRef.setInput('showViewToggle', false);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(host.querySelector('[aria-label="View mode"]')).toBeNull();
    });

    it('defaults to the card grid below 768px with no stored preference', async () => {
      // injectIsNarrowViewport reads matchMedia at construction, so stub before creating.
      stubMatchMedia(false);

      const f = TestBed.createComponent(InventoryItemsViewComponent);
      f.componentRef.setInput('rows', ROWS);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();

      expect(f.componentInstance.viewMode()).toBe('card');
      expect((f.nativeElement as HTMLElement).querySelector('app-inventory-card-grid')).not.toBeNull();
    });

    it('lets an explicit table preference win over the narrow-viewport card fallback', async () => {
      stubMatchMedia(false);
      setViewPref('table');

      const f = TestBed.createComponent(InventoryItemsViewComponent);
      f.componentRef.setInput('rows', ROWS);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();

      expect(f.componentInstance.viewMode()).toBe('table');
      const host = f.nativeElement as HTMLElement;
      expect(host.querySelector('app-inventory-table')).not.toBeNull();
      expect(host.querySelector('app-inventory-card-grid')).toBeNull();
    });
  });
});
