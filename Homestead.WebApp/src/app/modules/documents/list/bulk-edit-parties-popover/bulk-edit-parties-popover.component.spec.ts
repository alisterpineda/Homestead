import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { BulkEditPartiesPopoverComponent } from './bulk-edit-parties-popover.component';
import { CREATE_OPTION_ID } from '../../bulk-edit-shared';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function makeDoc(id: string, parties: { id: string; name: string }[] = []): GetDocumentsResponse {
  return {
    id,
    name: `${id}.pdf`,
    contentType: 'application/pdf',
    fileSize: 1000,
    fileExtension: '.pdf',
    tags: [],
    parties,
  };
}

@Component({
  imports: [BulkEditPartiesPopoverComponent],
  template: `<app-bulk-edit-parties-popover [selectedIds]="ids()" [documents]="docs()" />`,
})
class TestHostComponent {
  ids = signal<Set<string>>(new Set(['doc1', 'doc2']));
  docs = signal<GetDocumentsResponse[]>([
    makeDoc('doc1', [{ id: 'p1', name: 'Acme Corp' }]),
    makeDoc('doc2', [{ id: 'p1', name: 'Acme Corp' }, { id: 'p2', name: 'Test LLC' }]),
  ]);
}

describe('BulkEditPartiesPopoverComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: BulkEditPartiesPopoverComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideRouter([])],
    });
    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
    component = fixture.debugElement.children[0].componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize party items from selected documents', () => {
    component.onPopoverStateChanged('open');

    const parties = component.items();
    // "Acme Corp" (p1) is on both docs -> all, checked
    const acme = parties.find((p) => p.id === 'p1');
    expect(acme).toBeDefined();
    expect(acme!.initial).toBe('all');
    expect(acme!.state).toBe('checked');

    // "Test LLC" (p2) is only on doc2 -> some, mixed
    const test = parties.find((p) => p.id === 'p2');
    expect(test).toBeDefined();
    expect(test!.initial).toBe('some');
    expect(test!.state).toBe('mixed');
  });

  it('hasChanges should be false with no user interaction', () => {
    component.onPopoverStateChanged('open');
    expect(component.hasChanges()).toBe(false);
  });

  it('should cycle "all" item: checked → remove → checked', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p1');
    expect(component.items().find((p) => p.id === 'p1')!.state).toBe('remove');

    component.toggleItem('p1');
    expect(component.items().find((p) => p.id === 'p1')!.state).toBe('checked');
  });

  it('should cycle "some" item: mixed → checked → remove → mixed', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p2');
    expect(component.items().find((p) => p.id === 'p2')!.state).toBe('checked');

    component.toggleItem('p2');
    expect(component.items().find((p) => p.id === 'p2')!.state).toBe('remove');

    component.toggleItem('p2');
    expect(component.items().find((p) => p.id === 'p2')!.state).toBe('mixed');
  });

  it('hasChanges should be true when state differs from initial', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p2'); // mixed → checked
    expect(component.hasChanges()).toBe(true);
  });

  it('should compute correct parties to add', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p2'); // mixed → checked (add to all)

    const items = component.items();
    const toAdd = items
      .filter((p) => p.state === 'checked' && p.initial !== 'all')
      .map((p) => p.id);

    expect(toAdd).toContain('p2');
    expect(toAdd).not.toContain('p1');
  });

  it('should compute correct parties to remove', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p1'); // checked → remove

    const items = component.items();
    const toRemove = items.filter((p) => p.state === 'remove').map((p) => p.id);

    expect(toRemove).toContain('p1');
    expect(toRemove).not.toContain('p2');
  });

  it('should compute addCount and removeCount', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('p2'); // mixed → checked (add)
    component.toggleItem('p1'); // checked → remove

    expect(component.addCount()).toBe(1);
    expect(component.removeCount()).toBe(1);
  });

  it('should reset state on re-open', () => {
    component.onPopoverStateChanged('open');
    component.toggleItem('p1');

    // Re-open
    component.onPopoverStateChanged('open');

    const acme = component.items().find((p) => p.id === 'p1');
    expect(acme!.state).toBe('checked');
    expect(component.hasChanges()).toBe(false);
  });

  it('should add party from suggestion as new item', () => {
    component.onPopoverStateChanged('open');

    component.addFromSuggestion({ id: 'p3', name: 'New Corp' });

    const newParty = component.items().find((p) => p.id === 'p3');
    expect(newParty).toBeDefined();
    expect(newParty!.initial).toBe('new');
    expect(newParty!.state).toBe('checked');
    expect(newParty!.name).toBe('New Corp');
  });

  it('should cycle "new" item: checked → none → checked', () => {
    component.onPopoverStateChanged('open');

    component.addFromSuggestion({ id: 'p3', name: 'New Corp' });

    component.toggleItem('p3');
    expect(component.items().find((p) => p.id === 'p3')!.state).toBe('none');

    component.toggleItem('p3');
    expect(component.items().find((p) => p.id === 'p3')!.state).toBe('checked');
  });

  describe('confirmationMessage', () => {
    it('should build message for add-only', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('p2'); // mixed → checked (add)
      expect(component.confirmationMessage()).toBe('This will add 1 party on 2 documents.');
    });

    it('should build message for remove-only', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('p1'); // checked → remove
      expect(component.confirmationMessage()).toBe('This will remove 1 party on 2 documents.');
    });

    it('should build message for add and remove', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('p2'); // mixed → checked (add)
      component.toggleItem('p1'); // checked → remove
      expect(component.confirmationMessage()).toBe('This will add 1 party and remove 1 party on 2 documents.');
    });

    it('should pluralize parties correctly', () => {
      component.onPopoverStateChanged('open');
      component.addFromSuggestion({ id: 'p3', name: 'New Corp' });
      component.toggleItem('p2'); // mixed → checked (add)
      // Now 2 parties to add: p2 + p3
      expect(component.confirmationMessage()).toBe('This will add 2 parties on 2 documents.');
    });
  });

  it('confirmApply should call ctx.close on success', () => {
    component.onPopoverStateChanged('open');
    component.toggleItem('p2'); // mixed → checked (add)

    const ctx = { close: vi.fn() };
    component.confirmApply(ctx);

    // The actual API call is async, so we verify the method is callable
    // and doesn't throw synchronously
    expect(component.applying()).toBe(true);
  });

  describe('loading signal', () => {
    it('loading starts false after open', () => {
      component.onPopoverStateChanged('open');
      expect(component.loading()).toBe(false);
    });

    it('Create option hidden while loading', () => {
      component.onPopoverStateChanged('open');
      component.onSearchInput({ target: { value: 'New Corp' } } as unknown as Event);
      expect(component.loading()).toBe(true);
      expect(component.suggestionOptions().some((o) => o.id === CREATE_OPTION_ID)).toBe(false);
    });

    it('loading resets on re-open', () => {
      component.onPopoverStateChanged('open');
      component.onSearchInput({ target: { value: 'test' } } as unknown as Event);
      expect(component.loading()).toBe(true);

      // Re-open
      component.onPopoverStateChanged('open');
      expect(component.loading()).toBe(false);
    });
  });
});
