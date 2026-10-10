import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  DocumentTableComponent,
  HIDEABLE_COLUMNS,
  DEFAULT_COLUMN_VISIBILITY,
} from './document-table.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

function createMockDocument(overrides: Partial<GetDocumentsResponse> = {}): GetDocumentsResponse {
  return {
    id: crypto.randomUUID(),
    name: 'Test Document',
    contentType: 'application/pdf',
    fileSize: 1024,
    fileExtension: '.pdf',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    hasThumbnail: false,
    versionCount: 1,
    tags: [],
    parties: [],
    ...overrides,
  };
}

describe('DocumentTableComponent exports', () => {
  describe('HIDEABLE_COLUMNS', () => {
    it('should include all seven hideable column entries', () => {
      const ids = HIDEABLE_COLUMNS.map(c => c.id);
      expect(ids).toEqual(['parties', 'tags', 'contentType', 'fileSize', 'createdAt', 'updatedAt', 'documentDate']);
    });

    it('should label createdAt as "Date Added"', () => {
      const col = HIDEABLE_COLUMNS.find(c => c.id === 'createdAt');
      expect(col?.label).toBe('Date Added');
    });

    it('should label documentDate as "Document Date"', () => {
      const col = HIDEABLE_COLUMNS.find(c => c.id === 'documentDate');
      expect(col?.label).toBe('Document Date');
    });
  });

  describe('DEFAULT_COLUMN_VISIBILITY', () => {
    it('should hide createdAt by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['createdAt']).toBe(false);
    });

    it('should hide documentDate by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['documentDate']).toBe(false);
    });

    it('should only contain the three date columns', () => {
      expect(Object.keys(DEFAULT_COLUMN_VISIBILITY)).toEqual(['createdAt', 'updatedAt', 'documentDate']);
    });
  });
});

describe('DocumentTableComponent', () => {
  let fixture: ComponentFixture<DocumentTableComponent>;
  let component: DocumentTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
    fixture = TestBed.createComponent(DocumentTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('documents', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should define createdAt and documentDate columns', () => {
    fixture.detectChanges();
    const columnIds = component['_columns'].map(c => c.id);
    expect(columnIds).toContain('createdAt');
    expect(columnIds).toContain('documentDate');
  });

  it('should map createdAt column to CreatedAt sort key via sort change', () => {
    fixture.componentRef.setInput('sortBy', 'CreatedAt');
    fixture.componentRef.setInput('sortDir', 'Desc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'createdAt', desc: true }]);
  });

  it('should map documentDate column to DocumentDate sort key via sort change', () => {
    fixture.componentRef.setInput('sortBy', 'DocumentDate');
    fixture.componentRef.setInput('sortDir', 'Asc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'documentDate', desc: false }]);
  });

  it('should map updatedAt column to UpdatedAt sort key via sort change', () => {
    fixture.componentRef.setInput('sortBy', 'UpdatedAt');
    fixture.componentRef.setInput('sortDir', 'Desc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'updatedAt', desc: true }]);
  });

  it('should sync column visibility input to table state', () => {
    fixture.componentRef.setInput('columnVisibility', { createdAt: false, documentDate: true });
    fixture.detectChanges();

    const vis = component['_columnVisibility']();
    expect(vis['createdAt']).toBe(false);
    expect(vis['documentDate']).toBe(true);
  });

  it('should render the document name in the name column without a snippet', () => {
    const doc = createMockDocument({ name: 'Receipt.pdf', matchSnippet: null });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('tbody tr');
    expect(row).toBeTruthy();
    expect(row.textContent).toContain('Receipt.pdf');
    expect(row.querySelector('.match-snippet')).toBeNull();
  });

  it('should render the match snippet under the name when provided', () => {
    const doc = createMockDocument({
      name: 'Receipt.pdf',
      matchSnippet: 'annual <mark>tax</mark> payment summary',
    });
    fixture.componentRef.setInput('documents', [doc]);
    fixture.detectChanges();

    const row = fixture.nativeElement.querySelector('tbody tr');
    expect(row).toBeTruthy();
    expect(row.textContent).toContain('Receipt.pdf');
    const snippet = row.querySelector('.match-snippet');
    expect(snippet).toBeTruthy();
    expect(snippet.textContent).toContain('annual tax payment summary');
    const mark = snippet.querySelector('mark');
    expect(mark).toBeTruthy();
    expect(mark.textContent).toBe('tax');
  });

  describe('select-all header checkbox', () => {
    const docs = [createMockDocument({ id: 'a' }), createMockDocument({ id: 'b' })];

    function selectAllState(): string | null {
      return fixture.nativeElement.querySelector('[aria-label="Select all"]')?.getAttribute('aria-checked') ?? null;
    }

    beforeEach(() => fixture.componentRef.setInput('documents', docs));

    it('is unchecked when no rows are selected', () => {
      fixture.componentRef.setInput('selectedIds', new Set<string>());
      fixture.detectChanges();
      expect(selectAllState()).toBe('false');
    });

    it('is indeterminate when some but not all rows are selected', () => {
      fixture.componentRef.setInput('selectedIds', new Set(['a']));
      fixture.detectChanges();
      expect(selectAllState()).toBe('mixed');
    });

    it('is checked, not indeterminate, when every row is selected', () => {
      fixture.componentRef.setInput('selectedIds', new Set(['a', 'b']));
      fixture.detectChanges();
      expect(selectAllState()).toBe('true');
    });

    it('emits an empty selection when clicked with every row selected', () => {
      fixture.componentRef.setInput('selectedIds', new Set(['a', 'b']));
      const emitted: Set<string>[] = [];
      component.selectionChange.subscribe(ids => emitted.push(ids));
      fixture.detectChanges();

      fixture.nativeElement.querySelector('[aria-label="Select all"]').click();

      expect(emitted.at(-1)).toEqual(new Set());
    });

    it('emits every row id when clicked with nothing selected', () => {
      const emitted: Set<string>[] = [];
      component.selectionChange.subscribe(ids => emitted.push(ids));
      fixture.detectChanges();

      fixture.nativeElement.querySelector('[aria-label="Select all"]').click();

      expect(emitted.at(-1)).toEqual(new Set(['a', 'b']));
    });
  });

  it('emits the selected id when a row checkbox is clicked', () => {
    fixture.componentRef.setInput('documents', [createMockDocument({ id: 'a', name: 'Lease.pdf' })]);
    const emitted: Set<string>[] = [];
    component.selectionChange.subscribe(ids => emitted.push(ids));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[aria-label="Select Lease.pdf"]').click();

    expect(emitted.at(-1)).toEqual(new Set(['a']));
  });

  it('removes the id from the selection when a checked row checkbox is clicked', () => {
    fixture.componentRef.setInput('documents', [
      createMockDocument({ id: 'a', name: 'Lease.pdf' }),
      createMockDocument({ id: 'b', name: 'Deed.pdf' }),
    ]);
    fixture.componentRef.setInput('selectedIds', new Set(['a', 'b']));
    const emitted: Set<string>[] = [];
    component.selectionChange.subscribe(ids => emitted.push(ids));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[aria-label="Select Lease.pdf"]').click();

    expect(emitted.at(-1)).toEqual(new Set(['b']));
  });

  it('does not render the header of a column hidden by the visibility input', () => {
    fixture.componentRef.setInput('documents', [createMockDocument()]);
    fixture.componentRef.setInput('columnVisibility', { fileSize: false });
    fixture.detectChanges();

    const headers = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('thead th')).map(th => th.textContent?.trim());
    expect(headers).toContain('Content Type');
    expect(headers).not.toContain('File Size');
  });

  it('renders one body cell per visible header when a column is hidden', () => {
    fixture.componentRef.setInput('documents', [createMockDocument({ contentType: 'application/x-hidden-check' })]);
    fixture.componentRef.setInput('columnVisibility', { contentType: false });
    fixture.detectChanges();

    const headerCount = fixture.nativeElement.querySelectorAll('thead th').length;
    const row = fixture.nativeElement.querySelector('tbody tr') as HTMLElement;
    expect(row.querySelectorAll('td').length).toBe(headerCount);
    expect(row.textContent).not.toContain('application/x-hidden-check');
  });

  it('emits an ascending sort change when an unsorted sortable header is clicked', () => {
    const emitted: unknown[] = [];
    component.sortChange.subscribe(e => emitted.push(e));
    fixture.detectChanges();

    const nameButton = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('thead button')).find(
      b => b.textContent?.trim() === 'Name',
    );
    nameButton!.click();

    expect(emitted).toEqual([{ sortBy: 'Name', sortDir: 'Asc' }]);
  });

  it('emits a descending sort change when the ascending-sorted header is clicked', () => {
    fixture.componentRef.setInput('sortBy', 'Name');
    fixture.componentRef.setInput('sortDir', 'Asc');
    const emitted: unknown[] = [];
    component.sortChange.subscribe(e => emitted.push(e));
    fixture.detectChanges();

    const nameButton = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('thead button')).find(
      b => b.textContent?.trim() === 'Name',
    );
    nameButton!.click();

    expect(emitted).toEqual([{ sortBy: 'Name', sortDir: 'Desc' }]);
  });

  it('exposes each column width as a CSS variable on the container', () => {
    fixture.detectChanges();

    const container = fixture.nativeElement.querySelector('div.rounded-md.border') as HTMLElement;
    expect(container.style.getPropertyValue('--col-name-size')).toBe('300px');
    expect(container.style.getPropertyValue('--header-select-size')).toBe('48px');
  });

  it('widens a column and marks the table as resizing while its handle is dragged', () => {
    fixture.detectChanges();
    const container = fixture.nativeElement.querySelector('div.rounded-md.border') as HTMLElement;
    const nameTh = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('thead th')).find(
      th => th.textContent?.trim() === 'Name',
    )!;
    const handle = nameTh.querySelector('.cursor-col-resize') as HTMLElement;

    handle.dispatchEvent(new MouseEvent('mousedown', { clientX: 100, bubbles: true }));
    document.dispatchEvent(new MouseEvent('mousemove', { clientX: 150, bubbles: true }));
    fixture.detectChanges();

    expect(container.classList).toContain('select-none');
    expect(container.style.getPropertyValue('--col-name-size')).toBe('350px');

    document.dispatchEvent(new MouseEvent('mouseup', { clientX: 150, bubbles: true }));
    fixture.detectChanges();

    expect(container.classList).not.toContain('select-none');
    expect(container.style.getPropertyValue('--col-name-size')).toBe('350px');
  });
});
