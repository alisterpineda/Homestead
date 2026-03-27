import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
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

  describe('confirmationMessage', () => {
    it('should build message for add-only', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('invoices'); // mixed → checked (add)
      expect(component.confirmationMessage()).toBe('This will add 1 tag on 2 documents.');
    });

    it('should build message for remove-only', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('finance'); // checked → remove
      expect(component.confirmationMessage()).toBe('This will remove 1 tag on 2 documents.');
    });

    it('should build message for add and remove', () => {
      component.onPopoverStateChanged('open');
      component.toggleItem('invoices'); // mixed → checked (add)
      component.toggleItem('finance'); // checked → remove
      expect(component.confirmationMessage()).toBe('This will add 1 tag and remove 1 tag on 2 documents.');
    });

    it('should pluralize tags correctly', () => {
      component.onPopoverStateChanged('open');
      component.addFromSuggestion({ path: 'legal', effectiveColor: null } as any);
      component.toggleItem('invoices'); // mixed → checked (add)
      // Now 2 tags to add: invoices + legal
      expect(component.confirmationMessage()).toBe('This will add 2 tags on 2 documents.');
    });
  });

  it('confirmApply should set applying and show loading toast', () => {
    vi.spyOn(toast, 'promise').mockImplementation((promise) => {
      (promise as Promise<unknown>).catch(() => {});
      return 0 as never;
    });
    component.onPopoverStateChanged('open');
    component.toggleItem('invoices'); // mixed → checked (add)

    const ctx = { close: vi.fn() };
    component.confirmApply(ctx);

    expect(component.applying()).toBe(true);
    expect(toast.promise).toHaveBeenCalledWith(
      expect.any(Promise),
      expect.objectContaining({ loading: 'Updating documents...' }),
    );
  });

  describe('loading signal', () => {
    it('loading starts false after open', () => {
      component.onPopoverStateChanged('open');
      expect(component.loading()).toBe(false);
    });

    it('Create option hidden while loading', () => {
      component.onPopoverStateChanged('open');
      component.onSearchInput({ target: { value: 'newTag' } } as unknown as Event);
      expect(component.loading()).toBe(true);
      expect(component.suggestionOptions().some((o) => o.type === 'create')).toBe(false);
    });

    it('loading resets on re-open', () => {
      component.onPopoverStateChanged('open');
      component.onSearchInput({ target: { value: 'test' } } as unknown as Event);
      expect(component.loading()).toBe(true);

      // Re-open
      component.onPopoverStateChanged('open');
      expect(component.loading()).toBe(false);
    });

    it('selectOption with Create adds new item to items list', () => {
      component.onPopoverStateChanged('open');

      component.selectOption({ type: 'create', path: 'brand-new', effectiveColor: null });

      const newItem = component.items().find((t) => t.path === 'brand-new');
      expect(newItem).toBeDefined();
      expect(newItem!.initial).toBe('new');
      expect(newItem!.state).toBe('checked');
    });
  });
});
