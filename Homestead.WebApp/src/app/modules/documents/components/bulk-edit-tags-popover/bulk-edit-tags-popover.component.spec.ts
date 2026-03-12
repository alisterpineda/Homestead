import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { BulkEditTagsPopoverComponent } from './bulk-edit-tags-popover.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function makeDoc(id: string, tags: { path: string; color?: string }[] = []): GetDocumentsResponse {
  return {
    id,
    name: `${id}.pdf`,
    contentType: 'application/pdf',
    fileSize: 1000,
    fileExtension: '.pdf',
    tags: tags.map((t) => ({ id: t.path, name: t.path, path: t.path, effectiveColor: t.color ?? null })),
    parties: [],
  };
}

@Component({
  imports: [BulkEditTagsPopoverComponent],
  template: `<app-bulk-edit-tags-popover [selectedIds]="ids()" [documents]="docs()" />`,
})
class TestHostComponent {
  ids = signal<Set<string>>(new Set(['doc1', 'doc2']));
  docs = signal<GetDocumentsResponse[]>([
    makeDoc('doc1', [{ path: 'finance' }, { path: 'invoices' }]),
    makeDoc('doc2', [{ path: 'finance' }]),
  ]);
}

describe('BulkEditTagsPopoverComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: BulkEditTagsPopoverComponent;

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

  it('should initialize tag items from selected documents', () => {
    component.onPopoverStateChanged('open');

    const tags = component.items();
    // "finance" is on both docs -> all, checked
    const finance = tags.find((t) => t.path === 'finance');
    expect(finance).toBeDefined();
    expect(finance!.initial).toBe('all');
    expect(finance!.state).toBe('checked');

    // "invoices" is only on doc1 -> some, mixed
    const invoices = tags.find((t) => t.path === 'invoices');
    expect(invoices).toBeDefined();
    expect(invoices!.initial).toBe('some');
    expect(invoices!.state).toBe('mixed');
  });

  it('hasChanges should be false with no user interaction', () => {
    component.onPopoverStateChanged('open');
    expect(component.hasChanges()).toBe(false);
  });

  it('should cycle "all" item: checked → remove → checked', () => {
    component.onPopoverStateChanged('open');

    // finance starts as checked (initial: all)
    component.toggleItem('finance');
    expect(component.items().find((t) => t.path === 'finance')!.state).toBe('remove');

    component.toggleItem('finance');
    expect(component.items().find((t) => t.path === 'finance')!.state).toBe('checked');
  });

  it('should cycle "some" item: mixed → checked → remove → mixed', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('invoices');
    expect(component.items().find((t) => t.path === 'invoices')!.state).toBe('checked');

    component.toggleItem('invoices');
    expect(component.items().find((t) => t.path === 'invoices')!.state).toBe('remove');

    component.toggleItem('invoices');
    expect(component.items().find((t) => t.path === 'invoices')!.state).toBe('mixed');
  });

  it('hasChanges should be true when state differs from initial', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('invoices'); // mixed → checked
    expect(component.hasChanges()).toBe(true);
  });

  it('should compute correct tags to add', () => {
    component.onPopoverStateChanged('open');

    // Toggle "invoices" from mixed → checked (add to all)
    component.toggleItem('invoices');

    const items = component.items();
    const toAdd = items
      .filter((t) => t.state === 'checked' && t.initial !== 'all')
      .map((t) => t.path);

    expect(toAdd).toContain('invoices');
    expect(toAdd).not.toContain('finance');
  });

  it('should compute correct tags to remove', () => {
    component.onPopoverStateChanged('open');

    // Toggle "finance" from checked → remove
    component.toggleItem('finance');

    const items = component.items();
    const toRemove = items.filter((t) => t.state === 'remove').map((t) => t.path);

    expect(toRemove).toContain('finance');
    expect(toRemove).not.toContain('invoices');
  });

  it('should compute addCount and removeCount', () => {
    component.onPopoverStateChanged('open');

    component.toggleItem('invoices'); // mixed → checked (add)
    component.toggleItem('finance'); // checked → remove

    expect(component.addCount()).toBe(1);
    expect(component.removeCount()).toBe(1);
  });

  it('should reset state on re-open', () => {
    component.onPopoverStateChanged('open');
    component.toggleItem('finance');

    // Re-open
    component.onPopoverStateChanged('open');

    const finance = component.items().find((t) => t.path === 'finance');
    expect(finance!.state).toBe('checked');
    expect(component.hasChanges()).toBe(false);
  });

  it('should add tag from suggestion as new item', () => {
    component.onPopoverStateChanged('open');

    component.addFromSuggestion({ path: 'legal', effectiveColor: '#ff0000' } as any);

    const legal = component.items().find((t) => t.path === 'legal');
    expect(legal).toBeDefined();
    expect(legal!.initial).toBe('new');
    expect(legal!.state).toBe('checked');
    expect(legal!.color).toBe('#ff0000');
  });

  it('should cycle "new" item: checked → none → checked', () => {
    component.onPopoverStateChanged('open');

    component.addFromSuggestion({ path: 'legal', effectiveColor: null } as any);

    component.toggleItem('legal');
    expect(component.items().find((t) => t.path === 'legal')!.state).toBe('none');

    component.toggleItem('legal');
    expect(component.items().find((t) => t.path === 'legal')!.state).toBe('checked');
  });
});
