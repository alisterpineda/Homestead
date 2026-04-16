import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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
    description: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    documentCount: 0,
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

  describe('descriptionControl validators', () => {
    it('should be valid when empty', () => {
      component.descriptionControl.setValue('');
      expect(component.descriptionControl.valid).toBe(true);
    });

    it('should be valid with text', () => {
      component.descriptionControl.setValue('A useful description');
      expect(component.descriptionControl.valid).toBe(true);
    });

    it('should be invalid when exceeding 512 characters', () => {
      component.descriptionControl.setValue('a'.repeat(513));
      expect(component.descriptionControl.hasError('maxlength')).toBe(true);
      expect(component.descriptionControl.invalid).toBe(true);
    });

    it('should be valid at exactly 512 characters', () => {
      component.descriptionControl.setValue('a'.repeat(512));
      expect(component.descriptionControl.valid).toBe(true);
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

    it('should populate descriptionControl from tag input', () => {
      const tag = makeTag({ description: 'Some desc' });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.descriptionControl.value).toBe('Some desc');
      expect(component.descriptionControl.pristine).toBe(true);
      expect(component.descriptionControl.untouched).toBe(true);
    });

    it('should set descriptionControl to empty string when tag has null description', () => {
      const tag = makeTag({ description: null });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.descriptionControl.value).toBe('');
    });
  });

  describe('hasChanges', () => {
    beforeEach(() => {
      const tag = makeTag({ name: 'invoices', parentId: 'tag-parent', color: null, description: null });
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

    it('should be true when description changes', () => {
      component.descriptionControl.setValue('new description');
      expect(component.hasChanges()).toBe(true);
    });

    it('should be false when description set to empty matches null tag description', () => {
      component.descriptionControl.setValue('');
      expect(component.hasChanges()).toBe(false);
    });
  });
});
