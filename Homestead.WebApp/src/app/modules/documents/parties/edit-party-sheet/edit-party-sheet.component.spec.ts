import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EditPartySheetComponent } from './edit-party-sheet.component';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

function makeParty(overrides: Partial<GetPartiesResponse> = {}): GetPartiesResponse {
  return {
    id: 'party-1',
    name: 'Acme Corp',
    description: 'A test party',
    createdAt: '2025-01-15T00:00:00Z',
    updatedAt: '2025-01-15T00:00:00Z',
    documentCount: 0,
    ...overrides,
  };
}

describe('EditPartySheetComponent', () => {
  let fixture: ComponentFixture<EditPartySheetComponent>;
  let component: EditPartySheetComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    fixture = TestBed.createComponent(EditPartySheetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('nameControl validators', () => {
    it('should be invalid when empty', () => {
      component.nameControl.setValue('');
      expect(component.nameControl.hasError('required')).toBe(true);
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should be valid with a name', () => {
      component.nameControl.setValue('Acme Corp');
      expect(component.nameControl.valid).toBe(true);
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
      component.nameControl.setValue('New Name');
      expect(component.editName()).toBe('New Name');
    });
  });

  describe('state reset on open', () => {
    it('should populate nameControl from party input and mark pristine/untouched', () => {
      const party = makeParty({ name: 'Acme Corp' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.nameControl.value).toBe('Acme Corp');
      expect(component.nameControl.pristine).toBe(true);
      expect(component.nameControl.untouched).toBe(true);
    });

    it('should populate editDescription from party input', () => {
      const party = makeParty({ description: 'Some description' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.editDescription()).toBe('Some description');
    });
  });

  describe('hasChanges', () => {
    beforeEach(() => {
      const party = makeParty({ name: 'Acme Corp', description: 'A test party' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
    });

    it('should be false when nothing changed', () => {
      expect(component.hasChanges()).toBe(false);
    });

    it('should be true when name changes', () => {
      component.nameControl.setValue('New Name');
      expect(component.hasChanges()).toBe(true);
    });

    it('should be true when description changes', () => {
      component.editDescription.set('Updated description');
      expect(component.hasChanges()).toBe(true);
    });
  });

  describe('save disabled state', () => {
    beforeEach(() => {
      const party = makeParty({ name: 'Acme Corp' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();
    });

    it('should prevent save when name is empty (bug fix)', () => {
      component.nameControl.setValue('');
      expect(component.nameControl.invalid).toBe(true);
    });

    it('should prevent save when name exceeds max length', () => {
      component.nameControl.setValue('a'.repeat(257));
      expect(component.nameControl.invalid).toBe(true);
    });
  });
});
