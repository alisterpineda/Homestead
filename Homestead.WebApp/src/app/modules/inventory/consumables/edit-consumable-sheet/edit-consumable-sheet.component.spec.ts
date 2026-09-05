import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EditConsumableSheetComponent } from './edit-consumable-sheet.component';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { JsonPatchOperation } from '@/shared/json-patch';

// brn-select uses ResizeObserver in ngAfterViewInit.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeConsumable(overrides: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 'c-1',
    name: 'Wood screws',
    status: 'Active',
    unit: 'pcs',
    totalOnHand: 0,
    isLow: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    stockLocations: [],
    ...overrides,
  };
}

describe('EditConsumableSheetComponent', () => {
  let fixture: ComponentFixture<EditConsumableSheetComponent>;
  let component: EditConsumableSheetComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(EditConsumableSheetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function open(item: ConsumableResponse): void {
    fixture.componentRef.setInput('consumable', item);
    fixture.componentRef.setInput('state', 'open');
    fixture.detectChanges();
  }

  function ops(): JsonPatchOperation[] {
    return component['buildOps']() as JsonPatchOperation[];
  }

  it('seeds the brand from the item and emits no ops when unchanged', () => {
    open(makeConsumable({ brandId: 'apple', brandName: 'Apple' }));
    expect(component.editBrand()).toEqual({ id: 'apple', name: 'Apple' });
    expect(ops()).toEqual([]);
  });

  it('emits a /brandId op when a brand is picked', () => {
    open(makeConsumable());
    component.editBrand.set({ id: 'apple', name: 'Apple' });
    expect(ops()).toEqual([{ op: 'replace', path: '/brandId', value: 'apple' }]);
  });

  it('clears the brand by emitting /brandId:null', () => {
    open(makeConsumable({ brandId: 'apple', brandName: 'Apple' }));
    component.editBrand.set(null);
    expect(ops()).toEqual([{ op: 'replace', path: '/brandId', value: null }]);
  });

  describe('status note behaviour', () => {
    // The sheet renders into an overlay, so read the document body rather than the host element.
    function sheetHtml(): string {
      return (fixture.nativeElement as HTMLElement).ownerDocument.body
        .querySelector('hlm-sheet-content')?.innerHTML ?? '';
    }

    it('offers no status select and no note field on an active consumable', () => {
      open(makeConsumable());
      expect(sheetHtml()).not.toContain('consumable-status-note');
      expect(sheetHtml()).not.toContain('>Status<');
    });

    it('shows the note field only when the consumable is archived', () => {
      open(makeConsumable({ id: 'c-2', status: 'Archived', statusNote: 'Used up' }));
      expect(sheetHtml()).toContain('consumable-status-note');
      expect(component.editStatusNote()).toBe('Used up');
    });

    it('emits only /statusNote — never /status — when the note changes', () => {
      open(makeConsumable({ status: 'Archived', statusNote: 'Used up' }));
      component.editStatusNote.set('  Gave it away  ');
      expect(ops()).toEqual([{ op: 'replace', path: '/statusNote', value: 'Gave it away' }]);
    });

    it('clears the note by emitting /statusNote:null', () => {
      open(makeConsumable({ status: 'Archived', statusNote: 'Used up' }));
      component.editStatusNote.set('');
      expect(ops()).toEqual([{ op: 'replace', path: '/statusNote', value: null }]);
    });
  });
});
