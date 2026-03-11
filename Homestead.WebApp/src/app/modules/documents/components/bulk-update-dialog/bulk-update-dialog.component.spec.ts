import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { BulkUpdateDialogComponent } from './bulk-update-dialog.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function makeDoc(id: string, tags: { path: string; color?: string }[] = [], parties: { id: string; name: string }[] = []): GetDocumentsResponse {
  return {
    id,
    name: `${id}.pdf`,
    contentType: 'application/pdf',
    fileSize: 1000,
    fileExtension: '.pdf',
    tags: tags.map((t) => ({ id: t.path, name: t.path, path: t.path, effectiveColor: t.color ?? null })),
    parties: parties.map((p) => ({ id: p.id, name: p.name })),
  };
}

@Component({
  imports: [BulkUpdateDialogComponent],
  template: `<app-bulk-update-dialog [selectedIds]="ids()" [documents]="docs()" />`,
})
class TestHostComponent {
  ids = signal<Set<string>>(new Set(['doc1', 'doc2']));
  docs = signal<GetDocumentsResponse[]>([
    makeDoc('doc1', [{ path: 'finance' }, { path: 'invoices' }], [{ id: 'p1', name: 'Acme Corp' }]),
    makeDoc('doc2', [{ path: 'finance' }], [{ id: 'p1', name: 'Acme Corp' }, { id: 'p2', name: 'Test LLC' }]),
  ]);
}

describe('BulkUpdateDialogComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: BulkUpdateDialogComponent;

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

  it('should initialize tag checkboxes from selected documents', () => {
    component.onStateChanged('open');

    const tags = component.tagItems();
    // "finance" is on both docs -> all, checked
    const finance = tags.find((t) => t.path === 'finance');
    expect(finance).toBeDefined();
    expect(finance!.initial).toBe('all');
    expect(finance!.checked).toBe(true);
    expect(finance!.indeterminate).toBe(false);

    // "invoices" is only on doc1 -> some, indeterminate
    const invoices = tags.find((t) => t.path === 'invoices');
    expect(invoices).toBeDefined();
    expect(invoices!.initial).toBe('some');
    expect(invoices!.checked).toBe(false);
    expect(invoices!.indeterminate).toBe(true);
  });

  it('should initialize party checkboxes from selected documents', () => {
    component.onStateChanged('open');

    const parties = component.partyItems();
    // "Acme Corp" (p1) is on both docs -> all, checked
    const acme = parties.find((p) => p.id === 'p1');
    expect(acme).toBeDefined();
    expect(acme!.initial).toBe('all');
    expect(acme!.checked).toBe(true);
    expect(acme!.indeterminate).toBe(false);

    // "Test LLC" (p2) is only on doc2 -> some, indeterminate
    const test = parties.find((p) => p.id === 'p2');
    expect(test).toBeDefined();
    expect(test!.initial).toBe('some');
    expect(test!.checked).toBe(false);
    expect(test!.indeterminate).toBe(true);
  });

  it('hasChanges should be false with no user interaction', () => {
    component.onStateChanged('open');
    expect(component.hasChanges()).toBe(false);
  });

  it('hasChanges should be true when a checkbox is toggled', () => {
    component.onStateChanged('open');

    // Toggle indeterminate "invoices" tag to checked
    component.onTagCheckboxChange('invoices', true);

    expect(component.hasChanges()).toBe(true);
  });

  it('should compute correct tags to add on submit', () => {
    component.onStateChanged('open');

    // Toggle indeterminate "invoices" tag to checked (add to all)
    component.onTagCheckboxChange('invoices', true);

    const tags = component.tagItems();
    const toAdd = tags
      .filter((t) => t.checked && !t.indeterminate && t.initial !== 'all')
      .map((t) => t.path);

    expect(toAdd).toContain('invoices');
    expect(toAdd).not.toContain('finance'); // already on all, unchanged
  });

  it('should compute correct tags to remove on submit', () => {
    component.onStateChanged('open');

    // Uncheck "finance" (was on all docs)
    component.onTagCheckboxChange('finance', false);

    const tags = component.tagItems();
    const toRemove = tags
      .filter((t) => !t.checked && !t.indeterminate && t.initial !== 'new')
      .map((t) => t.path);

    expect(toRemove).toContain('finance');
    expect(toRemove).not.toContain('invoices'); // still indeterminate, not changed
  });

  it('should reset state on dialog open', () => {
    component.onStateChanged('open');

    // Make some changes
    component.onTagCheckboxChange('finance', false);
    component.onPartyCheckboxChange('p1', false);

    // Re-open
    component.onStateChanged('open');

    // Should be re-initialized
    const finance = component.tagItems().find((t) => t.path === 'finance');
    expect(finance!.checked).toBe(true);
    expect(finance!.initial).toBe('all');

    const acme = component.partyItems().find((p) => p.id === 'p1');
    expect(acme!.checked).toBe(true);
    expect(acme!.initial).toBe('all');

    expect(component.hasChanges()).toBe(false);
  });
});
