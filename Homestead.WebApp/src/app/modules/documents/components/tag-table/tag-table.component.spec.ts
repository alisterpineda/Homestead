import { TestBed, ComponentFixture } from '@angular/core/testing';
import {
  TagTableComponent,
  HIDEABLE_COLUMNS,
  DEFAULT_COLUMN_VISIBILITY,
} from './tag-table.component';

describe('TagTableComponent exports', () => {
  describe('HIDEABLE_COLUMNS', () => {
    it('should include path, description and createdAt', () => {
      const ids = HIDEABLE_COLUMNS.map(c => c.id);
      expect(ids).toEqual(['path', 'description', 'createdAt']);
    });

    it('should label path as "Path"', () => {
      const col = HIDEABLE_COLUMNS.find(c => c.id === 'path');
      expect(col?.label).toBe('Path');
    });

    it('should label description as "Description"', () => {
      const col = HIDEABLE_COLUMNS.find(c => c.id === 'description');
      expect(col?.label).toBe('Description');
    });

    it('should label createdAt as "Date Added"', () => {
      const col = HIDEABLE_COLUMNS.find(c => c.id === 'createdAt');
      expect(col?.label).toBe('Date Added');
    });
  });

  describe('DEFAULT_COLUMN_VISIBILITY', () => {
    it('should hide createdAt by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['createdAt']).toBe(false);
    });

    it('should only contain createdAt', () => {
      expect(Object.keys(DEFAULT_COLUMN_VISIBILITY)).toEqual(['createdAt']);
    });

    it('should not hide path by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['path']).toBeUndefined();
    });

    it('should not hide description by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['description']).toBeUndefined();
    });
  });
});

describe('TagTableComponent', () => {
  let fixture: ComponentFixture<TagTableComponent>;
  let component: TagTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(TagTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tags', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should define all expected columns', () => {
    fixture.detectChanges();
    const columnIds = component['_columns'].map(c => c.id);
    expect(columnIds).toEqual(['select', 'name', 'path', 'description', 'documentCount', 'createdAt']);
  });

  it('should map name column to Name sort key via sort input', () => {
    fixture.componentRef.setInput('sortBy', 'Name');
    fixture.componentRef.setInput('sortDir', 'Asc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'name', desc: false }]);
  });

  it('should map path column to Path sort key via sort input', () => {
    fixture.componentRef.setInput('sortBy', 'Path');
    fixture.componentRef.setInput('sortDir', 'Asc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'path', desc: false }]);
  });

  it('should map documentCount column to DocumentCount sort key via sort input', () => {
    fixture.componentRef.setInput('sortBy', 'DocumentCount');
    fixture.componentRef.setInput('sortDir', 'Desc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'documentCount', desc: true }]);
  });

  it('should map createdAt column to CreatedAt sort key via sort input', () => {
    fixture.componentRef.setInput('sortBy', 'CreatedAt');
    fixture.componentRef.setInput('sortDir', 'Desc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'createdAt', desc: true }]);
  });

  it('should clear sorting when sortBy is null', () => {
    fixture.componentRef.setInput('sortBy', 'Name');
    fixture.componentRef.setInput('sortDir', 'Asc');
    fixture.detectChanges();
    expect(component['_sorting']().length).toBe(1);

    fixture.componentRef.setInput('sortBy', null);
    fixture.detectChanges();
    expect(component['_sorting']()).toEqual([]);
  });

  it('should sync column visibility input to table state', () => {
    fixture.componentRef.setInput('columnVisibility', { createdAt: false, path: true });
    fixture.detectChanges();

    const vis = component['_columnVisibility']();
    expect(vis['createdAt']).toBe(false);
    expect(vis['path']).toBe(true);
  });

  it('should sync selected IDs input to row selection state', () => {
    fixture.componentRef.setInput('tags', [
      { id: 't1', name: 'A', path: 'A', documentCount: 0, createdAt: new Date().toISOString() },
      { id: 't2', name: 'B', path: 'B', documentCount: 0, createdAt: new Date().toISOString() },
    ]);
    fixture.componentRef.setInput('selectedIds', new Set(['t1']));
    fixture.detectChanges();

    const selection = component['_rowSelection']();
    expect(selection['t1']).toBe(true);
    expect(selection['t2']).toBeUndefined();
  });

  it('should emit sortChange when sorting changes via table', () => {
    fixture.componentRef.setInput('tags', []);
    fixture.detectChanges();

    let emitted: { sortBy: string; sortDir: string } | null | undefined;
    component.sortChange.subscribe(val => (emitted = val));

    const tableOptions = component['table'].options;
    const onSortingChange = tableOptions.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => [{ id: 'name', desc: false }]);

    expect(emitted).toEqual({ sortBy: 'Name', sortDir: 'Asc' });
  });

  it('should emit null sortChange when sorting is cleared', () => {
    fixture.componentRef.setInput('tags', []);
    fixture.detectChanges();

    let emitted: { sortBy: string; sortDir: string } | null | undefined;
    component.sortChange.subscribe(val => (emitted = val));

    const tableOptions = component['table'].options;
    const onSortingChange = tableOptions.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => []);

    expect(emitted).toBeNull();
  });

  it('should emit selectionChange when row selection changes via table', () => {
    fixture.componentRef.setInput('tags', [
      { id: 't1', name: 'A', path: 'A', documentCount: 0, createdAt: new Date().toISOString() },
    ]);
    fixture.detectChanges();

    let emitted: Set<string> | undefined;
    component.selectionChange.subscribe(val => (emitted = val));

    const tableOptions = component['table'].options;
    const onRowSelectionChange = tableOptions.onRowSelectionChange!;
    (onRowSelectionChange as (updater: unknown) => void)(() => ({ t1: true }));

    expect(emitted).toEqual(new Set(['t1']));
  });
});
