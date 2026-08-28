import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
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
const LADDER = makeItem({ id: 'ladder', name: 'Ladder', status: 'Lent' as ItemStatus, lentTo: 'Dave' });
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

  it('archives/restores via the matching API and signals changed', () => {
    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.onArchiveRestore(durableToRow(WRENCH));
    expect(durableApi.patchDurable).toHaveBeenCalled();
    expect(changed).toBe(1);

    component.onArchiveRestore(consumableToRow(TAPE));
    expect(consumableApi.patchConsumable).toHaveBeenCalled();
    expect(changed).toBe(2);
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
    let reset = 0;
    component.reset.subscribe(() => reset++);

    component.onReset();
    expect(component.showInactive()).toBe(false);
    expect(component.lowStockOnly()).toBe(false);
    expect(component.lentOnly()).toBe(false);
    expect(reset).toBe(1);
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
      vi.stubGlobal('matchMedia', vi.fn(() => ({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })));

      const f = TestBed.createComponent(InventoryItemsViewComponent);
      f.componentRef.setInput('rows', ROWS);
      f.detectChanges();
      await f.whenStable();
      f.detectChanges();

      expect(f.componentInstance.viewMode()).toBe('card');
      expect((f.nativeElement as HTMLElement).querySelector('app-inventory-card-grid')).not.toBeNull();
    });

    it('lets an explicit table preference win over the narrow-viewport card fallback', async () => {
      vi.stubGlobal('matchMedia', vi.fn(() => ({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })));
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
