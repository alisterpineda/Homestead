import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { TrackDurableSheetComponent } from './track-durable-sheet.component';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import type { StockOpLocation } from '../stock-op-sheet/stock-op-sheet.component';
import type { TrackDurableResponse } from '@/api/generated/models/track-durable-response';

const TRACK_RESULT: TrackDurableResponse = {
  detail: { consumable: { id: 's1', name: 'Drill bits', status: 'Active', unit: 'bits', totalOnHand: 4, isLow: false, createdAt: '', updatedAt: '' }, locations: [] },
  item: { id: 'i1', name: 'Drill bit #1', fungibility: 'NonFungible', status: 'Active', isContainer: false, path: [], createdAt: '', updatedAt: '' },
};

class StubConsumableApi {
  lastTrack: Parameters<InventoryConsumableApiService['trackDurable']>[0] | null = null;
  trackCalls = 0;
  trackDurable = (p: Parameters<InventoryConsumableApiService['trackDurable']>[0]) => {
    this.lastTrack = p;
    this.trackCalls++;
    return of(TRACK_RESULT);
  };
}

const AREA_SOURCE: StockOpLocation = { areaId: 'a1', containerItemId: null, label: 'Garage', quantity: 5 };
const CONTAINER_SOURCE: StockOpLocation = { areaId: null, containerItemId: 'c1', label: 'Garage › Bin', quantity: 5 };

describe('TrackDurableSheetComponent', () => {
  let fixture: ComponentFixture<TrackDurableSheetComponent>;
  let component: TrackDurableSheetComponent;
  let api: StubConsumableApi;

  function setup(from: StockOpLocation): void {
    // Allow a test to re-seed with a different source location.
    TestBed.resetTestingModule();
    api = new StubConsumableApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: InventoryConsumableApiService, useValue: api },
      ],
    });
    fixture = TestBed.createComponent(TrackDurableSheetComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('consumableId', 's1');
    fixture.componentRef.setInput('consumableName', 'Drill bits');
    fixture.componentRef.setInput('unit', 'bits');
    fixture.componentRef.setInput('fromLocation', from);
    fixture.componentRef.setInput('state', 'open');
  }

  it('prefills the name with the consumable name', () => {
    setup(AREA_SOURCE);
    expect(component.name()).toBe('Drill bits');
  });

  it('only treats a container source as compartment-eligible', () => {
    setup(AREA_SOURCE);
    expect(component['isContainerSource']()).toBe(false);

    setup(CONTAINER_SOURCE);
    expect(component['isContainerSource']()).toBe(true);
  });

  it('submits the area source with a null compartment', () => {
    setup(AREA_SOURCE);
    component.name.set('Bit A');
    component.compartment.set('ignored');
    component.note.set('a note');
    component.handleSubmit();

    expect(api.lastTrack).toEqual({
      id: 's1',
      body: { areaId: 'a1', containerItemId: null, name: 'Bit A', compartment: null, note: 'a note' },
    });
  });

  it('submits the container source with its compartment', () => {
    setup(CONTAINER_SOURCE);
    component.name.set('Bit B');
    component.compartment.set('Top tray');
    component.handleSubmit();

    expect(api.lastTrack).toEqual({
      id: 's1',
      body: { areaId: null, containerItemId: 'c1', name: 'Bit B', compartment: 'Top tray', note: null },
    });
  });

  it('emits the result on success', () => {
    setup(AREA_SOURCE);
    let emitted: TrackDurableResponse | null = null;
    component.created.subscribe((r) => (emitted = r));
    component.handleSubmit();
    expect(emitted).toBe(TRACK_RESULT);
  });

  it('does not submit with a blank name', () => {
    setup(AREA_SOURCE);
    component.name.set('   ');
    expect(component.canSubmit()).toBe(false);
    component.handleSubmit();
    expect(api.trackCalls).toBe(0);
  });

  it('does not double-submit while a submit is in flight', () => {
    setup(AREA_SOURCE);
    component.saving.set(true);
    component.handleSubmit();
    expect(api.trackCalls).toBe(0);
  });
});
