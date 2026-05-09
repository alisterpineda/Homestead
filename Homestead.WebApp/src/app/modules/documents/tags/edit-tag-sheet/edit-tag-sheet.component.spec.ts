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

  describe('name validity', () => {
    it('should be invalid when empty', () => {
      component.editName.set('');
      expect(component.nameRequiredError()).toBe(true);
      expect(component.nameInvalid()).toBe(true);
    });

    it('should be valid with a plain name', () => {
      component.editName.set('invoices');
      expect(component.nameInvalid()).toBe(false);
    });

    it('should be invalid when name contains a slash', () => {
      component.editName.set('finance/invoices');
      expect(component.namePatternError()).toBe(true);
      expect(component.nameInvalid()).toBe(true);
    });

    it('should be invalid when name exceeds 256 characters', () => {
      component.editName.set('a'.repeat(257));
      expect(component.nameMaxLenError()).toBe(true);
      expect(component.nameInvalid()).toBe(true);
    });

    it('should be valid at exactly 256 characters', () => {
      component.editName.set('a'.repeat(256));
      expect(component.nameInvalid()).toBe(false);
    });
  });

  describe('previewPath', () => {
    it('reflects editName changes', () => {
      component.editName.set('receipts');
      expect(component.previewPath()).toBe('receipts');
    });
  });

  describe('seeding from tag input', () => {
    it('populates editName/editDescription/editParentId/editColor from the tag input on open', () => {
      const tag = makeTag({ name: 'invoices', description: 'Some desc', parentId: 'p1', color: '#abcdef' });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.editName()).toBe('invoices');
      expect(component.editDescription()).toBe('Some desc');
      expect(component.editParentId()).toBe('p1');
      expect(component.editColor()).toBe('#abcdef');
    });

    it('seeds editDescription to empty string when tag has null description', () => {
      const tag = makeTag({ description: null });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.editDescription()).toBe('');
    });

    it('reseeds editName when the tag input changes to a different entity', () => {
      const first = makeTag({ id: 'tag-1', name: 'invoices' });
      fixture.componentRef.setInput('tag', first);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
      expect(component.editName()).toBe('invoices');

      // user mutates the field
      component.editName.set('user-edit');
      expect(component.editName()).toBe('user-edit');

      // swap to a different tag — seed key changes, signal resets to new entity name
      const second = makeTag({ id: 'tag-2', name: 'receipts' });
      fixture.componentRef.setInput('tag', second);
      fixture.detectChanges();

      expect(component.editName()).toBe('receipts');
    });

    it('preserves a pending edit when the same tag reference is re-pushed (refresh-while-editing)', () => {
      const tag = makeTag({ id: 'tag-1', name: 'invoices' });
      fixture.componentRef.setInput('tag', tag);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editName.set('user-edit');

      // simulate parent re-pushing a fresh reference for the same entity id
      const refreshed = makeTag({ id: 'tag-1', name: 'invoices' });
      fixture.componentRef.setInput('tag', refreshed);
      fixture.detectChanges();

      expect(component.editName()).toBe('user-edit');
    });
  });

  describe('description validity', () => {
    it('should be valid when empty', () => {
      component.editDescription.set('');
      expect(component.descriptionInvalid()).toBe(false);
    });

    it('should be valid with text', () => {
      component.editDescription.set('A useful description');
      expect(component.descriptionInvalid()).toBe(false);
    });

    it('should be invalid when exceeding 512 characters', () => {
      component.editDescription.set('a'.repeat(513));
      expect(component.descriptionInvalid()).toBe(true);
    });

    it('should be valid at exactly 512 characters', () => {
      component.editDescription.set('a'.repeat(512));
      expect(component.descriptionInvalid()).toBe(false);
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
      component.editName.set('receipts');
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
      component.editDescription.set('new description');
      expect(component.hasChanges()).toBe(true);
    });

    it('should be false when description set to empty matches null tag description', () => {
      component.editDescription.set('');
      expect(component.hasChanges()).toBe(false);
    });
  });
});
