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

  describe('name validity', () => {
    it('should be invalid when empty', () => {
      component.editName.set('');
      expect(component.nameRequiredError()).toBe(true);
      expect(component.nameInvalid()).toBe(true);
    });

    it('should be valid with a name', () => {
      component.editName.set('Acme Corp');
      expect(component.nameInvalid()).toBe(false);
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

  describe('seeding from party input', () => {
    it('populates editName from party input on open', () => {
      const party = makeParty({ name: 'Acme Corp' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.editName()).toBe('Acme Corp');
    });

    it('populates editDescription from party input on open', () => {
      const party = makeParty({ description: 'Some description' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      expect(component.editDescription()).toBe('Some description');
    });

    it('reseeds editName when the party input changes to a different entity', () => {
      const first = makeParty({ id: 'party-1', name: 'Acme Corp' });
      fixture.componentRef.setInput('party', first);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editName.set('user-edit');
      expect(component.editName()).toBe('user-edit');

      const second = makeParty({ id: 'party-2', name: 'Beta Inc' });
      fixture.componentRef.setInput('party', second);
      fixture.detectChanges();

      expect(component.editName()).toBe('Beta Inc');
    });

    it('preserves a pending edit when the same party reference is re-pushed (refresh-while-editing)', () => {
      const party = makeParty({ id: 'party-1', name: 'Acme Corp' });
      fixture.componentRef.setInput('party', party);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editName.set('user-edit');

      const refreshed = makeParty({ id: 'party-1', name: 'Acme Corp' });
      fixture.componentRef.setInput('party', refreshed);
      fixture.detectChanges();

      expect(component.editName()).toBe('user-edit');
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
      component.editName.set('New Name');
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

    it('should report invalid name when empty (bug fix)', () => {
      component.editName.set('');
      expect(component.nameInvalid()).toBe(true);
    });

    it('should report invalid name when exceeding max length', () => {
      component.editName.set('a'.repeat(257));
      expect(component.nameInvalid()).toBe(true);
    });
  });
});
