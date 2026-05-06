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
});
