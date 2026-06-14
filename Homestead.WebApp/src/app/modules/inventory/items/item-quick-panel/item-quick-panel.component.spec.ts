import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError, type Observable } from 'rxjs';
import { ItemQuickPanelComponent } from './item-quick-panel.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';

function makeItem(over: Partial<ItemResponse> = {}): ItemResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    areaId: 'garage',
    areaName: 'Garage',
    path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
    resolvedAreaId: 'garage',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...over,
  };
}

interface PatchCall {
  id: string;
  body: JsonPatchDocument;
}

class StubItemApi {
  patches: PatchCall[] = [];
  getItemsCalls: unknown[] = [];
  patchResult: ItemResponse = makeItem();
  failNext: unknown = null;
  contentsTotal = 4;

  patchItem = (p: PatchCall): Observable<ItemResponse> => {
    this.patches.push(p);
    if (this.failNext) {
      const err = this.failNext;
      this.failNext = null;
      return throwError(() => err);
    }
    return of(this.patchResult);
  };

  getItems = (p: unknown) => {
    this.getItemsCalls.push(p);
    return of({ items: [], page: 1, pageSize: 1, totalCount: this.contentsTotal });
  };
}

class StubErrorHandler {
  handled: unknown[] = [];
  isServerOrNetworkError = () => false;
  handleError = (err: unknown) => this.handled.push(err);
}

describe('ItemQuickPanelComponent', () => {
  let fixture: ComponentFixture<ItemQuickPanelComponent>;
  let component: ItemQuickPanelComponent;
  let api: StubItemApi;
  let errorHandler: StubErrorHandler;

  beforeEach(() => {
    api = new StubItemApi();
    errorHandler = new StubErrorHandler();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: InventoryItemApiService, useValue: api },
        { provide: ErrorHandlerService, useValue: errorHandler },
      ],
    });
    fixture = TestBed.createComponent(ItemQuickPanelComponent);
    component = fixture.componentInstance;
  });

  async function open(item = makeItem()): Promise<void> {
    fixture.componentRef.setInput('item', item);
    fixture.componentRef.setInput('state', 'open');
    fixture.detectChanges();
    await fixture.whenStable();
  }

  describe('buildMoveOps', () => {
    it('returns no ops when the destination matches the current location', async () => {
      await open();
      expect(component.buildMoveOps({ kind: 'area', id: 'garage' })).toEqual([]);
      expect(component.canConfirmMove()).toBe(false); // moveDest seeds to the current location
    });

    it('emits both pair ops on a cross-kind move (area → container)', async () => {
      await open();
      expect(component.buildMoveOps({ kind: 'container', id: 'chest' })).toEqual([
        { op: 'replace', path: '/areaId', value: null },
        { op: 'replace', path: '/parentItemId', value: 'chest' },
      ]);
    });

    it('emits only the changed op when unplacing an area-placed item', async () => {
      await open();
      expect(component.buildMoveOps({ kind: 'unplaced' })).toEqual([
        { op: 'replace', path: '/areaId', value: null },
      ]);
    });

    it('clears a stale compartment when moving to a DIFFERENT container', async () => {
      await open(makeItem({
        areaId: null, areaName: null, parentItemId: 'chest', compartment: 'Top drawer',
        path: [
          { id: 'garage', name: 'Garage', kind: 'Area' },
          { id: 'chest', name: 'Tool chest', kind: 'Container' },
        ],
      }));
      expect(component.buildMoveOps({ kind: 'container', id: 'bin' })).toEqual([
        { op: 'replace', path: '/parentItemId', value: 'bin' },
        { op: 'replace', path: '/compartment', value: null },
      ]);
    });

    it('does not touch the compartment when moving to an area (server auto-clears it)', async () => {
      await open(makeItem({
        areaId: null, areaName: null, parentItemId: 'chest', compartment: 'Top drawer', path: [],
      }));
      expect(component.buildMoveOps({ kind: 'area', id: 'garage' })).toEqual([
        { op: 'replace', path: '/areaId', value: 'garage' },
        { op: 'replace', path: '/parentItemId', value: null },
      ]);
    });
  });

  describe('move flow', () => {
    it('patches the move ops and overwrites local state with the response', async () => {
      await open();
      let changedCount = 0;
      component.changed.subscribe(() => changedCount++);
      api.patchResult = makeItem({ areaId: null, areaName: null, parentItemId: 'chest' });

      component.openMove();
      component.moveDest.set({ kind: 'container', id: 'chest' });
      component.confirmMove();

      expect(api.patches).toEqual([{
        id: 'item-1',
        body: [
          { op: 'replace', path: '/areaId', value: null },
          { op: 'replace', path: '/parentItemId', value: 'chest' },
        ],
      }]);
      expect(component.view()?.parentItemId).toBe('chest');
      expect(changedCount).toBe(1);
      expect(component['inlineForm']()).toBeNull();
    });

    it('seeds the move destination from the current location', async () => {
      await open(makeItem({ areaId: null, areaName: null, parentItemId: 'chest' }));
      component.openMove();
      expect(component.moveDest()).toEqual({ kind: 'container', id: 'chest' });
    });

    it('reseeds the destination when move is reopened after a cancel', async () => {
      await open(makeItem({ areaId: null, areaName: null, parentItemId: 'chest' }));
      component.openMove();
      component.moveDest.set({ kind: 'area', id: 'garage' });
      component.closeForm();

      // Close→reopen round-trips the seed key to the same string; the per-open nonce must still
      // force a reseed so the abandoned draft does not leak into the new opening.
      component.openMove();
      expect(component.moveDest()).toEqual({ kind: 'container', id: 'chest' });
    });
  });

  describe('status flow', () => {
    it('ignores picking the current status', async () => {
      await open();
      component.onStatusPick('Active');
      expect(api.patches.length).toBe(0);
    });

    it('patches /status only for non-Lent statuses (server clears lentTo)', async () => {
      await open(makeItem({ status: 'Lent', lentTo: 'Dave' }));
      component.onStatusPick('Active');
      expect(api.patches).toEqual([{
        id: 'item-1',
        body: [{ op: 'replace', path: '/status', value: 'Active' }],
      }]);
    });

    it('keeps terminal items actionable — a status pick on an Archived item still patches', async () => {
      await open(makeItem({ status: 'Archived' }));
      component.onStatusPick('Active');
      expect(api.patches.length).toBe(1);
    });

    it('collects lentTo inline and patches /status + /lentTo together', async () => {
      await open();
      component.onStatusPick('Lent');
      expect(api.patches.length).toBe(0); // no patch yet — form opened
      expect(component['inlineForm']()).toBe('lent');

      component.confirmLent();
      expect(api.patches.length).toBe(0); // blank lentTo blocked

      component.lentToDraft.set('  Dave next door  ');
      component.confirmLent();
      expect(api.patches).toEqual([{
        id: 'item-1',
        body: [
          { op: 'replace', path: '/status', value: 'Lent' },
          { op: 'replace', path: '/lentTo', value: 'Dave next door' },
        ],
      }]);
    });

    it('ignores a late patch response after the panel moved to another item', async () => {
      await open(); // item-1
      const pending = new Subject<ItemResponse>();
      api.patchItem = ((p: PatchCall) => {
        api.patches.push(p);
        return pending.asObservable();
      }) as StubItemApi['patchItem'];

      component.onStatusPick('Sold'); // PATCH for item-1 in flight

      fixture.componentRef.setInput('item', makeItem({ id: 'item-2', name: 'Other' }));
      fixture.detectChanges();
      await fixture.whenStable();

      pending.next(makeItem({ id: 'item-1', status: 'Sold' })); // late response
      pending.complete();

      expect(component.view()?.id).toBe('item-2'); // not clobbered by item-1's response
      expect(component.view()?.status).toBe('Active');
    });

    it('keeps the in-flight guard when switching inline forms (no concurrent PATCH)', async () => {
      await open();
      const pending = new Subject<ItemResponse>();
      api.patchItem = ((p: PatchCall) => {
        api.patches.push(p);
        return pending.asObservable();
      }) as StubItemApi['patchItem'];

      component.onStatusPick('Sold'); // PATCH in flight, saving = true
      component.openMove();           // must NOT reseed the saving guard
      component.moveDest.set({ kind: 'container', id: 'chest' });
      component.confirmMove();        // blocked while the first PATCH is in flight

      expect(api.patches.length).toBe(1);
      pending.next(makeItem({ status: 'Sold' }));
      pending.complete();
    });

    it('toasts the failure and keeps the panel state on a patch error', async () => {
      await open();
      api.failNext = { status: 400 };
      component.onStatusPick('Sold');
      expect(errorHandler.handled.length).toBe(1);
      expect(component.view()?.status).toBe('Active'); // unchanged
    });
  });

  describe('contents count', () => {
    it('fetches a PageSize-1 children count for containers when opened', async () => {
      await open(makeItem({ isContainer: true }));
      expect(api.getItemsCalls).toEqual([
        { ParentItemId: 'item-1', IncludeInactive: true, Page: 1, PageSize: 1 },
      ]);
      expect(component.contentsCount()).toBe(4);
    });

    it('does not fetch contents for non-containers', async () => {
      await open(makeItem({ isContainer: false }));
      expect(api.getItemsCalls.length).toBe(0);
      expect(component.contentsCount()).toBeNull();
    });
  });

  describe('outputs', () => {
    it('emits viewContents with the current item', async () => {
      const item = makeItem({ isContainer: true });
      await open(item);
      let emitted: ItemResponse | undefined;
      component.viewContents.subscribe((v) => (emitted = v));
      component.onViewContents();
      expect(emitted).toEqual(item);
    });

    it('emits closed when the sheet closes', async () => {
      await open();
      let closedCount = 0;
      component.closed.subscribe(() => closedCount++);
      component.onStateChanged('closed');
      expect(closedCount).toBe(1);
    });
  });
});
