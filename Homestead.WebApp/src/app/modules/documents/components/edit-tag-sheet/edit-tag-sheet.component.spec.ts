import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { EditTagSheetComponent } from './edit-tag-sheet.component';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

// brn-select uses ResizeObserver in ngAfterViewInit
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeTag(overrides: Partial<GetTagsResponse> = {}): GetTagsResponse {
  return {
    id: 'tag-1',
    name: 'invoices',
    path: 'finance/invoices',
    parentId: 'tag-parent',
    color: null,
    effectiveColor: null,
    ...overrides,
  };
}

describe('EditTagSheetComponent', () => {
  let fixture: ComponentFixture<EditTagSheetComponent>;
  let component: EditTagSheetComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiConfiguration, useValue: { rootUrl: '' } },
      ],
    });
    fixture = TestBed.createComponent(EditTagSheetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('nameControl validators', () => {
    it('should be invalid when empty', () => {
      component.nameControl.setValue('');
      expect(component.nameControl.hasError('required')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be valid with a plain name', () => {
      component.nameControl.setValue('invoices');
      expect(component.nameControl.valid).toBe(true);
    });

    it('should be invalid when name contains a slash', () => {
      component.nameControl.setValue('finance/invoices');
      expect(component.nameControl.hasError('pattern')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be invalid when name exceeds 256 characters', () => {
      component.nameControl.setValue('a'.repeat(257));
      expect(component.nameControl.hasError('maxlength')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be valid at exactly 256 characters', () => {
      component.nameControl.setValue('a'.repeat(256));
      expect(component.nameControl.valid).toBe(true);
    });
  });

  describe('toSignal bridge', () => {
    it('editName signal reflects FormControl value changes', () => {
      component.nameControl.setValue('updated');
      expect(component.editName()).toBe('updated');
    });

    it('previewPath uses the bridged signal', () => {
      component.nameControl.setValue('receipts');
      expect(component.previewPath()).toBe('receipts');
    });
  });

  describe('state reset on open', () => {
    it('should populate nameControl from tag input and mark pristine/untouched', () => {
      const tag = makeTag({ name: 'invoices' });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.nameControl.value).toBe('invoices');
      expect(component.nameControl.pristine).toBe(true);
      expect(component.nameControl.untouched).toBe(true);
    });
  });

  describe('hasChanges', () => {
    beforeEach(() => {
      const tag = makeTag({ name: 'invoices', parentId: 'tag-parent', color: null });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
    });

    it('should be false when nothing changed', () => {
      expect(component.hasChanges()).toBe(false);
    });

    it('should be true when name changes', () => {
      component.nameControl.setValue('receipts');
      expect(component.hasChanges()).toBe(true);
    });

    it('should be true when parent changes', () => {
      component.editParentId.set(null);
      expect(component.hasChanges()).toBe(true);
    });

    it('should be true when color changes', () => {
      component.editColor.set('#ff0000');
      expect(component.hasChanges()).toBe(true);
    });
  });
});
