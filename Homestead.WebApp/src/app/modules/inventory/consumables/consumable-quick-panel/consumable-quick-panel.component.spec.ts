import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError, type Observable } from 'rxjs';
import { ConsumableQuickPanelComponent } from './consumable-quick-panel.component';
import { stockLocationKey } from './stock-stepper.engine';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';

function makeConsumable(over: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 's1',
    name: 'Paper towels',
    unit: 'rolls',
    status: 'Active',
    totalOnHand: 5.5,
    isLow: false,
    restockThreshold: 2,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...over,
  };
}

const PANTRY: StockEntryResponse = {
  id: 'e1', areaId: 'pantry', containerItemId: null, locationLabel: 'Pantry', path: [], quantity: 5,
};
const BIN: StockEntryResponse = {
  id: 'e2', areaId: null, containerItemId: 'bin', locationLabel: 'Bin', path: [], quantity: 0.5,
};

function makeDetail(over: Partial<ConsumableDetailResponse> = {}): ConsumableDetailResponse {
  return { consumable: makeConsumable(), locations: [PANTRY, BIN], ...over };
}

interface ApiCall {
  method: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  params: any;
}

class StubConsumableApi {
  calls: ApiCall[] = [];
  detail = makeDetail();
  failNext: unknown = null;

  private respond(method: string, params: unknown): Observable<ConsumableDetailResponse> {
    this.calls.push({ method, params });
    if (this.failNext) {
      const err = this.failNext;
      this.failNext = null;
      return throwError(() => err);
    }
    return of(this.detail);
  }

  // Detail responses mirror the requested id so identity guards can be asserted.
  getConsumable = (p: { id: string }): Observable<ConsumableDetailResponse> => {
    this.calls.push({ method: 'getConsumable', params: p });
    if (this.failNext) {
      const err = this.failNext;
      this.failNext = null;
      return throwError(() => err);
    }
    return of({ ...this.detail, consumable: { ...this.detail.consumable, id: p.id } });
  };
  receiveStock = (p: unknown) => this.respond('receiveStock', p);
  consumeStock = (p: unknown) => this.respond('consumeStock', p);
  transferStock = (p: unknown) => this.respond('transferStock', p);
  adjustStock = (p: unknown) => this.respond('adjustStock', p);

  callsOf(method: string): ApiCall[] {
    return this.calls.filter((c) => c.method === method);
  }
}

class StubErrorHandler {
  handled: unknown[] = [];
  isServerOrNetworkError = () => false;
  handleError = (err: unknown) => this.handled.push(err);
  getErrorMessage = () => 'error';
}

describe('ConsumableQuickPanelComponent', () => {
  let fixture: ComponentFixture<ConsumableQuickPanelComponent>;
  let component: ConsumableQuickPanelComponent;
  let api: StubConsumableApi;
  let errorHandler: StubErrorHandler;

  const KEY_PANTRY = stockLocationKey(PANTRY);

  beforeEach(() => {
    api = new StubConsumableApi();
    errorHandler = new StubErrorHandler();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: InventoryConsumableApiService, useValue: api },
        { provide: ErrorHandlerService, useValue: errorHandler },
      ],
    });
    fixture = TestBed.createComponent(ConsumableQuickPanelComponent);
    component = fixture.componentInstance;
  });

  async function open(consumable = makeConsumable()): Promise<void> {
    fixture.componentRef.setInput('consumable', consumable);
    fixture.componentRef.setInput('state', 'open');
    fixture.detectChanges();
    await fixture.whenStable();
  }

  // Engine taps debounce on a real timer; tests commit them deterministically via flush.
  function flushSteppers(): void {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (component as any).engine.flushAll();
  }

  describe('detail fetch', () => {
    it('does not fetch while closed', async () => {
      fixture.componentRef.setInput('consumable', makeConsumable());
      fixture.detectChanges();
      await fixture.whenStable();
      expect(api.callsOf('getConsumable').length).toBe(0);
    });

    it('fetches the detail when opened and exposes its locations', async () => {
      await open();
      expect(api.callsOf('getConsumable')).toEqual([{ method: 'getConsumable', params: { id: 's1' } }]);
      expect(component.entries()).toEqual([PANTRY, BIN]);
      expect(component.consumableView()).toEqual(makeConsumable());
    });

    it('falls back to the row data for the header before the fetch lands', () => {
      fixture.componentRef.setInput('consumable', makeConsumable({ name: 'Row name' }));
      expect(component.consumableView()?.name).toBe('Row name');
    });
  });

  describe('steppers', () => {
    it('shows optimistic per-entry and total quantities', async () => {
      await open();
      component.onTap(PANTRY, -1);
      component.onTap(PANTRY, -1);
      expect(component.displayedQty(PANTRY)).toBe(3);
      expect(component.displayedQty(BIN)).toBe(0.5);
      expect(component.displayedTotal()).toBe(3.5);
    });

    it('commits a positive net delta as one receiveStock call shaped like the stock-op sheet', async () => {
      await open();
      let changedCount = 0;
      component.changed.subscribe(() => changedCount++);

      component.onTap(PANTRY, 1);
      component.onTap(PANTRY, 1);
      component.onTap(PANTRY, 1);
      flushSteppers();

      expect(api.callsOf('receiveStock')).toEqual([{
        method: 'receiveStock',
        params: {
          id: 's1',
          body: {
            areaId: 'pantry',
            containerItemId: null,
            quantity: 3,
            unitCost: null,
            unitCostCurrencyId: null,
            note: null,
          },
        },
      }]);
      expect(changedCount).toBe(1);
      expect(component.displayedQty(PANTRY)).toBe(5); // response is the new base, delta cleared
    });

    it('commits a negative net delta as one consumeStock call', async () => {
      await open();
      component.onTap(BIN, -1);
      flushSteppers();

      expect(api.callsOf('consumeStock')).toEqual([{
        method: 'consumeStock',
        params: {
          id: 's1',
          body: { areaId: null, containerItemId: 'bin', quantity: 1, note: null },
        },
      }]);
    });

    it('makes no API call for a net-zero burst', async () => {
      await open();
      component.onTap(PANTRY, 1);
      component.onTap(PANTRY, -1);
      flushSteppers();
      expect(api.callsOf('receiveStock').length).toBe(0);
      expect(api.callsOf('consumeStock').length).toBe(0);
    });

    it('clears the optimistic delta, toasts, and refetches when a commit fails', async () => {
      await open();
      api.failNext = { status: 400 };
      component.onTap(PANTRY, -1);
      flushSteppers();
      await fixture.whenStable();

      expect(errorHandler.handled.length).toBe(1);
      expect(component.displayedQty(PANTRY)).toBe(5);
      expect(api.callsOf('getConsumable').length).toBe(2); // initial + resync
    });
  });

  describe('summary derivations', () => {
    it('clamps restockPercent to 100 and tracks optimistic taps', async () => {
      await open(); // total 5.5, threshold 2 → way past 100
      expect(component.restockPercent()).toBe(100);

      component.onTap(PANTRY, -1);
      component.onTap(PANTRY, -1);
      component.onTap(PANTRY, -1);
      component.onTap(PANTRY, -1);
      // displayed total 1.5 of threshold 2 → 75
      expect(component.restockPercent()).toBe(75);
      expect(component.isLowNow()).toBe(true);
    });

    it('returns null restockPercent when no threshold is set', async () => {
      api.detail = makeDetail({ consumable: makeConsumable({ restockThreshold: null }) });
      await open(makeConsumable({ restockThreshold: null }));
      expect(component.restockPercent()).toBeNull();
      expect(component.isLowNow()).toBe(false);
    });

    it('flags terminal consumables as archived (read-only)', async () => {
      api.detail = makeDetail({ consumable: makeConsumable({ status: 'Archived' }) });
      await open(makeConsumable({ status: 'Archived' }));
      expect(component.archived()).toBe(true);

      // The badge reaches the title row only via the `panelTitleEnd` projection slot; a misspelled
      // attribute would silently drop it into the body, so pin it through the portaled DOM.
      const body = (fixture.nativeElement as HTMLElement).ownerDocument.body;
      const titleRow = body.querySelector('hlm-sheet-content [hlmsheettitle]')!.parentElement!;
      expect(titleRow.textContent).toContain('Paper towels');
      expect(titleRow.querySelector('app-item-status-badge')).not.toBeNull();
    });
  });

  describe('inline assign form', () => {
    it('requires a destination and positive quantity, then receives into the picked place', async () => {
      await open();
      component.openAssign();
      expect(component.formError()).toBe('Enter a quantity.');

      component.assignQty.set('2');
      expect(component.formError()).toBe('Pick a location.');

      component.assignDest.set({ kind: 'container', id: 'shelf' });
      expect(component.canSubmitForm()).toBe(true);

      component.submitForm();
      expect(api.callsOf('receiveStock')[0].params).toEqual({
        id: 's1',
        body: {
          areaId: null,
          containerItemId: 'shelf',
          quantity: 2,
          unitCost: null,
          unitCostCurrencyId: null,
          note: null,
        },
      });
      expect(component['inlineForm']()).toBeNull(); // closed on success
    });

    it('reseeds a blank form when assign is reopened after a successful submit', async () => {
      await open();
      component.openAssign();
      component.assignQty.set('40');
      component.assignDest.set({ kind: 'area', id: 'garage' });
      component.submitForm();
      expect(component['inlineForm']()).toBeNull();

      // Close→reopen round-trips the seed key to the same string; the per-open nonce must still
      // force a reseed so the previous draft (40) does not leak into the new opening.
      component.openAssign();
      expect(component.assignQty()).toBe('');
      expect(component.assignDest()).toEqual({ kind: 'unplaced' });
    });
  });

  describe('inline move form', () => {
    it('prefills the full on-hand and blocks a same-as-source destination', async () => {
      await open();
      component.openMove(PANTRY);
      expect(component.moveQty()).toBe('5');

      component.moveDest.set({ kind: 'area', id: 'pantry' });
      expect(component.formError()).toBe('Destination must differ from the source.');

      component.moveDest.set({ kind: 'area', id: 'garage' });
      expect(component.canSubmitForm()).toBe(true);

      component.submitForm();
      expect(api.callsOf('transferStock')[0].params).toEqual({
        id: 's1',
        body: {
          fromAreaId: 'pantry',
          fromContainerItemId: null,
          toAreaId: 'garage',
          toContainerItemId: null,
          quantity: 5,
          note: null,
        },
      });
    });

    it('flushes pending taps before opening and snapshots the displayed quantity', async () => {
      await open();
      component.onTap(PANTRY, 1);
      component.openMove(PANTRY);

      // The pending +1 was flushed as its own commit...
      expect(api.callsOf('receiveStock').length).toBe(1);
      // ...and the prefill reflects the optimistic quantity at click time.
      expect(component.moveQty()).toBe('6');
    });

    it('re-prefills from the entry when move is reopened on the same location', async () => {
      await open();
      component.openMove(PANTRY);
      component.moveQty.set('1');
      component.closeForm();

      component.openMove(PANTRY);
      expect(component.moveQty()).toBe('5'); // fresh prefill, not the abandoned draft
    });

    it('blocks a move quantity above the on-hand snapshot before submit', async () => {
      await open();
      component.openMove(PANTRY); // on-hand snapshot is 5
      component.moveDest.set({ kind: 'area', id: 'garage' });
      component.moveQty.set('12');
      expect(component.formError()).toBe('Only 5 on hand here.');
      expect(component.canSubmitForm()).toBe(false);
    });
  });

  describe('inline correct form', () => {
    it('prefills the current quantity and blocks the no-op the server would reject', async () => {
      await open();
      component.openCorrect(BIN);
      expect(component.correctQty()).toBe('0.5');
      expect(component.formError()).toBe('Enter a different quantity to correct.');

      component.correctQty.set('-1');
      expect(component.formError()).toBe('Quantity must be zero or positive.');

      component.correctQty.set('3');
      component.submitForm();
      expect(api.callsOf('adjustStock')[0].params).toEqual({
        id: 's1',
        body: { areaId: null, containerItemId: 'bin', newQuantity: 3, note: null },
      });
    });
  });

  describe('stale-state guards', () => {
    it('queued commits use the consumable id captured at tap time, not the current panel subject', async () => {
      await open(); // s1
      const first = new Subject<ConsumableDetailResponse>();
      const received: { id: string }[] = [];
      api.receiveStock = ((p: { id: string }) => {
        received.push(p);
        return received.length === 1 ? first.asObservable() : of(api.detail);
      }) as StubConsumableApi['receiveStock'];

      component.onTap(PANTRY, 1);
      flushSteppers(); // commit 1 in flight
      component.onTap(BIN, 1);
      flushSteppers(); // commit 2 queued behind it

      // Panel moves on to another consumable while the queue is still draining.
      fixture.componentRef.setInput('consumable', makeConsumable({ id: 's2', name: 'Other' }));
      fixture.detectChanges();
      await fixture.whenStable();

      first.next(makeDetail());
      first.complete();

      expect(received.map((p) => p.id)).toEqual(['s1', 's1']);
    });

    it('ignores a late commit response for a consumable the panel no longer shows', async () => {
      await open(); // s1
      const pending = new Subject<ConsumableDetailResponse>();
      api.receiveStock = (() => pending.asObservable()) as StubConsumableApi['receiveStock'];

      component.onTap(PANTRY, 1);
      flushSteppers(); // in flight for s1

      fixture.componentRef.setInput('consumable', makeConsumable({ id: 's2', name: 'Other' }));
      fixture.detectChanges();
      await fixture.whenStable();
      expect(component.consumableView()?.id).toBe('s2');

      pending.next(makeDetail()); // s1's truth, arriving late
      pending.complete();

      expect(component.consumableView()?.id).toBe('s2'); // not clobbered by s1's response
    });

    it('commits the destination validated at submit time even if the form is cancelled while queued', async () => {
      await open();
      const pending = new Subject<ConsumableDetailResponse>();
      api.consumeStock = ((p: unknown) => {
        api.calls.push({ method: 'consumeStock', params: p });
        return pending.asObservable();
      }) as StubConsumableApi['consumeStock'];

      component.onTap(PANTRY, -1);
      flushSteppers(); // holds the queue

      component.openAssign();
      component.assignQty.set('2');
      component.assignDest.set({ kind: 'area', id: 'garage' });
      component.submitForm(); // queued behind the consume
      component.closeForm();  // reseeds assignDest to 'unplaced'

      pending.next(makeDetail());
      pending.complete(); // queue advances → receive runs

      const receive = api.callsOf('receiveStock')[0].params as { body: { areaId: string | null } };
      expect(receive.body.areaId).toBe('garage');
    });

    it('discards a stale open fetch that resolves after a newer commit response', async () => {
      const pendingGet = new Subject<ConsumableDetailResponse>();
      api.getConsumable = ((p: { id: string }) => {
        api.calls.push({ method: 'getConsumable', params: p });
        return pendingGet.asObservable();
      }) as StubConsumableApi['getConsumable'];

      fixture.componentRef.setInput('consumable', makeConsumable());
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
      await fixture.whenStable(); // open GET still in flight

      component.onTap(PANTRY, 1);
      flushSteppers(); // commit lands synchronously → detail = [PANTRY, BIN]
      expect(component.entries().length).toBe(2);

      pendingGet.next(makeDetail({ locations: [PANTRY] })); // pre-commit snapshot
      pendingGet.complete();

      expect(component.entries().length).toBe(2); // stale GET discarded
    });
  });

  describe('close behaviour', () => {
    it('flushes pending taps and emits closed when the sheet closes', async () => {
      await open();
      let closedCount = 0;
      component.closed.subscribe(() => closedCount++);

      component.onTap(PANTRY, 1);
      component.onStateChanged('closed');

      expect(api.callsOf('receiveStock').length).toBe(1);
      expect(closedCount).toBe(1);
    });
  });
});
