import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { CollapseItemsDialogComponent } from './collapse-items-dialog.component';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { SupplyDetailResponse } from '@/api/generated/models/supply-detail-response';

const DETAIL: SupplyDetailResponse = {
  supply: { id: 's1', name: 'Screws', status: 'Active', unit: 'screws', totalOnHand: 5, isLow: false, createdAt: '', updatedAt: '' },
  locations: [],
};

function makeItem(over: Partial<ItemResponse>): ItemResponse {
  return {
    id: 'x', name: 'Item', fungibility: 'NonFungible', status: 'Active',
    isContainer: false, path: [], createdAt: '', updatedAt: '', ...over,
  };
}

const ITEM_A = makeItem({ id: 'a', name: 'Screw A' });
const ITEM_B = makeItem({ id: 'b', name: 'Screw B' });

class StubItemApi {
  getItems = () => of({ items: [ITEM_A, ITEM_B], page: 1, pageSize: 50, totalCount: 2 });
}

class StubSupplyApi {
  lastCollapse: Parameters<InventorySupplyApiService['collapseItems']>[0] | null = null;
  collapseCalls = 0;
  collapseItems = (p: Parameters<InventorySupplyApiService['collapseItems']>[0]) => {
    this.lastCollapse = p;
    this.collapseCalls++;
    return of(DETAIL);
  };
}

describe('CollapseItemsDialogComponent', () => {
  let fixture: ComponentFixture<CollapseItemsDialogComponent>;
  let component: CollapseItemsDialogComponent;
  let api: StubSupplyApi;

  beforeEach(() => {
    api = new StubSupplyApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InventoryItemApiService, useValue: new StubItemApi() },
        { provide: InventorySupplyApiService, useValue: api },
      ],
    });
    fixture = TestBed.createComponent(CollapseItemsDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('supplyId', 's1');
    fixture.componentRef.setInput('supplyName', 'Screws');
    fixture.componentRef.setInput('unit', 'screws');
  });

  it('requires a selection and a target before it can collapse', () => {
    expect(component['canCollapse']()).toBe(false);

    component['toggle'](ITEM_A);
    expect(component['selectedCount']()).toBe(1);
    // Still no target location picked.
    expect(component['canCollapse']()).toBe(false);

    component['targetLocation'].set({ kind: 'area', id: 'area1' });
    expect(component['canCollapse']()).toBe(true);
  });

  it('toggles a selection off again', () => {
    component['toggle'](ITEM_A);
    component['toggle'](ITEM_B);
    expect(component['selectedCount']()).toBe(2);
    component['toggle'](ITEM_A);
    expect(component['selectedCount']()).toBe(1);
    expect(component['isSelected']('a')).toBe(false);
  });

  it('blocks when the target container is one of the selected items', () => {
    component['toggle'](ITEM_A);
    component['targetLocation'].set({ kind: 'container', id: 'a' });
    expect(component['validationError']()).not.toBeNull();
    expect(component['canCollapse']()).toBe(false);
  });

  it('does not call the API until the confirm is invoked', () => {
    component['toggle'](ITEM_A);
    component['targetLocation'].set({ kind: 'area', id: 'area1' });
    // Opening the destructive confirm alone must not fire the request.
    component['confirmState'].set('open');
    expect(api.collapseCalls).toBe(0);
  });

  it('does not double-submit while a collapse is in flight', () => {
    component['toggle'](ITEM_A);
    component['targetLocation'].set({ kind: 'area', id: 'area1' });
    component['collapsing'].set(true);
    component.handleCollapse({ close: () => {} });
    expect(api.collapseCalls).toBe(0);
  });

  it('itemDetailLabel surfaces value and serial, else null', () => {
    expect(component['itemDetailLabel'](makeItem({ value: 2000, valueCurrencySymbol: '$', serial: 'SN1' }))).toBe('$2000 · SN SN1');
    expect(component['itemDetailLabel'](makeItem({ value: 50 }))).toBe('50');
    expect(component['itemDetailLabel'](makeItem({}))).toBeNull();
  });

  it('collapses the selected ids into the target and emits', () => {
    let emitted: SupplyDetailResponse | null = null;
    component.collapsed.subscribe((d) => (emitted = d));

    component['toggle'](ITEM_A);
    component['toggle'](ITEM_B);
    component['targetLocation'].set({ kind: 'area', id: 'area1' });

    let closed = false;
    component.handleCollapse({ close: () => (closed = true) });

    expect(api.lastCollapse).toEqual({
      id: 's1',
      body: { itemIds: ['a', 'b'], areaId: 'area1', containerItemId: null },
    });
    expect(closed).toBe(true);
    expect(emitted).toBe(DETAIL);
  });
});
