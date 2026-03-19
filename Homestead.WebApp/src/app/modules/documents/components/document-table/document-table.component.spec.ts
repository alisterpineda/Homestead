import { TestBed, ComponentFixture } from '@angular/core/testing';
import {
  DocumentTableComponent,
  HIDEABLE_COLUMNS,
  DEFAULT_COLUMN_VISIBILITY,
} from './document-table.component';

describe('DocumentTableComponent exports', () => {
  describe('HIDEABLE_COLUMNS', () => {
    it('should include all six hideable column entries', () => {
      const ids = HIDEABLE_COLUMNS.map(c => c.id);
      expect(ids).toEqual(['parties', 'tags', 'contentType', 'fileSize', 'createdAt', 'documentDate']);
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

    it('should only contain the two date columns', () => {
      expect(Object.keys(DEFAULT_COLUMN_VISIBILITY)).toEqual(['createdAt', 'documentDate']);
    });
  });
});

describe('DocumentTableComponent', () => {
  let fixture: ComponentFixture<DocumentTableComponent>;
  let component: DocumentTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
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

  it('should sync column visibility input to table state', () => {
    fixture.componentRef.setInput('columnVisibility', { createdAt: false, documentDate: true });
    fixture.detectChanges();

    const vis = component['_columnVisibility']();
    expect(vis['createdAt']).toBe(false);
    expect(vis['documentDate']).toBe(true);
  });
});
