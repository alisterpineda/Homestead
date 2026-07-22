import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EditDurableSheetComponent } from './edit-durable-sheet.component';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { JsonPatchOperation } from '@/shared/json-patch';

// brn-select uses ResizeObserver in ngAfterViewInit
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(overrides: Partial<DurableResponse> = {}): DurableResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    path: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('EditDurableSheetComponent', () => {
  let fixture: ComponentFixture<EditDurableSheetComponent>;
  let component: EditDurableSheetComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(EditDurableSheetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function open(item: DurableResponse): void {
    fixture.componentRef.setInput('durable', item);
    fixture.componentRef.setInput('state', 'open');
    fixture.detectChanges();
  }

  function ops(): JsonPatchOperation[] {
    return component['buildOps']() as JsonPatchOperation[];
  }

  describe('seeding', () => {
    it('populates the form fields from the item on open', () => {
      open(makeItem({
        name: 'Camera', serial: 'SN-1', acquiredOn: '2025-01-02',
        value: 1200, valueCurrencyId: 'cad', areaId: 'garage', notes: 'Mine',
      }));

      expect(component.editName()).toBe('Camera');
      expect(component.editSerial()).toBe('SN-1');
      expect(component.editAcquiredOn()).toBe('2025-01-02');
      expect(component.editValue()).toBe('1200');
      expect(component.editCurrencyId()).toBe('cad');
      expect(component.editAreaId()).toBe('garage');
      expect(component.editNotes()).toBe('Mine');
      expect(component.editStatus()).toBe('Active');
    });

    it('seeds empty/null fields to empty strings', () => {
      open(makeItem());
      expect(component.editSerial()).toBe('');
      expect(component.editValue()).toBe('');
      expect(component.editCurrencyId()).toBeNull();
      expect(component.editAreaId()).toBeNull();
    });

    it('reseeds the form when reopened for the same item after being closed', () => {
      open(makeItem({ name: 'Drill' }));
      expect(component.editName()).toBe('Drill');

      // Edit the draft, then close. While closed the portal is destroyed, so nothing
      // reads the field — the linkedSignal never observes the intermediate closed key.
      component.editName.set('Renamed');
      fixture.componentRef.setInput('state', 'closed');
      fixture.detectChanges();

      // Reopen for the same item: the draft must reseed to source, not stay 'Renamed'.
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
      expect(component.editName()).toBe('Drill');
    });

    it('seeds the container / parent / compartment fields from the item', () => {
      open(makeItem({
        parentItemId: 'chest', isContainer: true, compartment: 'Top drawer',
        path: [
          { id: 'garage', name: 'Garage', kind: 'Area' },
          { id: 'chest', name: 'Tool chest', kind: 'Container' },
        ],
      }));
      expect(component.editParentItemId()).toBe('chest');
      expect(component.editIsContainer()).toBe(true);
      expect(component.editCompartment()).toBe('Top drawer');
    });
  });

  describe('containment buildOps', () => {
    it('moves area→container by emitting the [/areaId:null, /parentItemId] pair', () => {
      open(makeItem({ areaId: 'garage', areaName: 'Garage' }));
      component['onLocationChange']({ kind: 'container', id: 'chest' });
      expect(ops()).toEqual([
        { op: 'replace', path: '/areaId', value: null },
        { op: 'replace', path: '/parentItemId', value: 'chest' },
      ]);
    });

    it('moves container→area by emitting the [/areaId, /parentItemId:null] pair', () => {
      open(makeItem({ parentItemId: 'chest' }));
      component['onLocationChange']({ kind: 'area', id: 'garage' });
      expect(ops()).toEqual([
        { op: 'replace', path: '/areaId', value: 'garage' },
        { op: 'replace', path: '/parentItemId', value: null },
      ]);
    });

    it('unplaces from a container by emitting only /parentItemId:null', () => {
      open(makeItem({ parentItemId: 'chest', compartment: 'Top drawer' }));
      component['onLocationChange']({ kind: 'unplaced' });
      expect(ops()).toEqual([{ op: 'replace', path: '/parentItemId', value: null }]);
    });

    it('emits a /compartment op only while in a container', () => {
      open(makeItem({ parentItemId: 'chest', compartment: 'Top drawer' }));
      component.editCompartment.set('Bottom shelf');
      expect(ops()).toEqual([{ op: 'replace', path: '/compartment', value: 'Bottom shelf' }]);
    });

    it('suppresses the /compartment op when the item is not in a container', () => {
      open(makeItem({ areaId: 'garage' }));
      component.editCompartment.set('orphan note');
      expect(ops()).toEqual([]);
    });

    it('emits a /isContainer op when the container flag is turned on', () => {
      open(makeItem({ isContainer: false }));
      component.editIsContainer.set(true);
      expect(ops()).toEqual([{ op: 'replace', path: '/isContainer', value: true }]);
    });

    it('emits a /isContainer op when the container flag is turned off', () => {
      open(makeItem({ isContainer: true }));
      component.editIsContainer.set(false);
      expect(ops()).toEqual([{ op: 'replace', path: '/isContainer', value: false }]);
    });

    it('seeds editIsContainer disabled-relevant hasComponents from the item', () => {
      // The Storage-container checkbox is disabled when the item already holds components (an assembly
      // can't be flipped to a container); the sheet reads hasComponents straight off the item.
      open(makeItem({ isContainer: false, hasComponents: true }));
      expect(component.durable()?.hasComponents).toBe(true);
      expect(component.editIsContainer()).toBe(false);
    });
  });

  describe('hasChanges / buildOps', () => {
    it('produces no ops when nothing changed', () => {
      open(makeItem({ name: 'Drill' }));
      expect(component.hasChanges()).toBe(false);
      expect(ops()).toEqual([]);
    });

    it('emits a replace op only for the changed name', () => {
      open(makeItem({ name: 'Drill' }));
      component.editName.set('Hammer');
      expect(ops()).toEqual([{ op: 'replace', path: '/name', value: 'Hammer' }]);
    });

    it('seeds brand and model from the item and emits no ops when unchanged', () => {
      open(makeItem({ brandId: 'apple', brandName: 'Apple', model: 'MRW23' }));
      expect(component.editBrand()).toEqual({ id: 'apple', name: 'Apple' });
      expect(component.editModel()).toBe('MRW23');
      expect(ops()).toEqual([]);
    });

    it('emits a /model op (trimmed) when the model changes', () => {
      open(makeItem());
      component.editModel.set('  MRW23  ');
      expect(ops()).toEqual([{ op: 'replace', path: '/model', value: 'MRW23' }]);
    });

    it('emits a /brandId op when a brand is picked', () => {
      open(makeItem());
      component.editBrand.set({ id: 'apple', name: 'Apple' });
      expect(ops()).toEqual([{ op: 'replace', path: '/brandId', value: 'apple' }]);
    });

    it('clears the brand by emitting /brandId:null', () => {
      open(makeItem({ brandId: 'apple', brandName: 'Apple' }));
      component.editBrand.set(null);
      expect(ops()).toEqual([{ op: 'replace', path: '/brandId', value: null }]);
    });

    it('moves an item by replacing /areaId', () => {
      open(makeItem({ areaId: 'garage', areaName: 'Garage' }));
      component.editAreaId.set('office');
      expect(ops()).toEqual([{ op: 'replace', path: '/areaId', value: 'office' }]);
    });

    it('unplaces an item by replacing /areaId with null', () => {
      open(makeItem({ areaId: 'garage', areaName: 'Garage' }));
      component.editAreaId.set(null);
      expect(ops()).toEqual([{ op: 'replace', path: '/areaId', value: null }]);
    });

    it('sends value and currency together as a pair', () => {
      open(makeItem({ value: 100, valueCurrencyId: 'cad' }));
      component.editValue.set('250');
      expect(ops()).toEqual([
        { op: 'replace', path: '/value', value: 250 },
        { op: 'replace', path: '/valueCurrencyId', value: 'cad' },
      ]);
    });

    it('clears value and currency together when value is emptied', () => {
      open(makeItem({ value: 100, valueCurrencyId: 'cad' }));
      component.editValue.set('');
      expect(ops()).toEqual([
        { op: 'replace', path: '/value', value: null },
        { op: 'replace', path: '/valueCurrencyId', value: null },
      ]);
    });
  });

  describe('acquisition fields', () => {
    it('seeds the acquisition fields from the item on open', () => {
      open(makeItem({
        acquisitionKind: 'Purchased', acquisitionCost: 189, acquisitionCostCurrencyId: 'cad',
        acquiredFrom: 'Home Depot',
      }));

      expect(component.editAcquisitionKind()).toBe('Purchased');
      expect(component.editAcquisitionCost()).toBe('189');
      expect(component.editAcquisitionCurrencyId()).toBe('cad');
      expect(component.editAcquiredFrom()).toBe('Home Depot');
    });

    it('seeds unset acquisition fields to their empty defaults', () => {
      open(makeItem());
      expect(component.editAcquisitionKind()).toBe('');
      expect(component.editAcquisitionCost()).toBe('');
      expect(component.editAcquisitionCurrencyId()).toBeNull();
      expect(component.editAcquiredFrom()).toBe('');
    });

    it('emits an /acquisitionKind op when a kind is picked', () => {
      open(makeItem());
      component.editAcquisitionKind.set('Gift');
      expect(ops()).toEqual([{ op: 'replace', path: '/acquisitionKind', value: 'Gift' }]);
    });

    it('clears the kind by emitting /acquisitionKind:null', () => {
      open(makeItem({ acquisitionKind: 'Purchased' }));
      component.editAcquisitionKind.set('');
      expect(ops()).toEqual([{ op: 'replace', path: '/acquisitionKind', value: null }]);
    });

    it('emits an /acquiredFrom op (trimmed) when the source changes', () => {
      open(makeItem());
      component.editAcquiredFrom.set('  Home Depot  ');
      expect(ops()).toEqual([{ op: 'replace', path: '/acquiredFrom', value: 'Home Depot' }]);
    });

    it('sends purchase price and currency together as a pair', () => {
      open(makeItem({ acquisitionCost: 100, acquisitionCostCurrencyId: 'cad' }));
      component.editAcquisitionCost.set('250');
      expect(ops()).toEqual([
        { op: 'replace', path: '/acquisitionCost', value: 250 },
        { op: 'replace', path: '/acquisitionCostCurrencyId', value: 'cad' },
      ]);
    });

    it('clears purchase price and currency together when the price is emptied', () => {
      open(makeItem({ acquisitionCost: 100, acquisitionCostCurrencyId: 'cad' }));
      component.editAcquisitionCost.set('');
      expect(ops()).toEqual([
        { op: 'replace', path: '/acquisitionCost', value: null },
        { op: 'replace', path: '/acquisitionCostCurrencyId', value: null },
      ]);
    });

    it('rejects a price without a currency', () => {
      open(makeItem());
      component.editAcquisitionCost.set('50');
      expect(component.validationError()).toBe('Pick a currency for the purchase price.');
      expect(component.canSave()).toBe(false);
    });
  });

  describe('LentTo behaviour', () => {
    it('requires LentTo when status is Lent', () => {
      open(makeItem({ status: 'Active' }));
      component.editStatus.set('Lent');
      expect(component.validationError()).not.toBeNull();
      expect(component.canSave()).toBe(false);
    });

    it('sends status and lentTo together when lending', () => {
      open(makeItem({ status: 'Active' }));
      component.editStatus.set('Lent');
      component.editLentTo.set('Dave next door');
      expect(component.validationError()).toBeNull();
      expect(ops()).toEqual([
        { op: 'replace', path: '/status', value: 'Lent' },
        { op: 'replace', path: '/lentTo', value: 'Dave next door' },
      ]);
    });

    it('does not send a lentTo op when leaving Lent (server clears it)', () => {
      open(makeItem({ status: 'Lent', lentTo: 'Dave' }));
      component.editStatus.set('Active');
      expect(ops()).toEqual([{ op: 'replace', path: '/status', value: 'Active' }]);
    });
  });

  describe('validation', () => {
    it('rejects an empty name', () => {
      open(makeItem());
      component.editName.set('   ');
      expect(component.validationError()).toBe('Name is required.');
    });

    it('rejects a value without a currency', () => {
      open(makeItem());
      component.editValue.set('50');
      expect(component.validationError()).toBe('Pick a currency for the value.');
    });
  });
});
