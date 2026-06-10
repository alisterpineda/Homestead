import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EditItemSheetComponent } from './edit-item-sheet.component';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { JsonPatchOperation } from '@/shared/json-patch';

// brn-select uses ResizeObserver in ngAfterViewInit
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeItem(overrides: Partial<ItemResponse> = {}): ItemResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('EditItemSheetComponent', () => {
  let fixture: ComponentFixture<EditItemSheetComponent>;
  let component: EditItemSheetComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(EditItemSheetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function open(item: ItemResponse): void {
    fixture.componentRef.setInput('item', item);
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
