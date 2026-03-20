import { TestBed, ComponentFixture } from '@angular/core/testing';
import {
  PartyTableComponent,
  HIDEABLE_COLUMNS,
  DEFAULT_COLUMN_VISIBILITY,
} from './party-table.component';

describe('PartyTableComponent exports', () => {
  describe('HIDEABLE_COLUMNS', () => {
    it('should include description and createdAt', () => {
      const ids = HIDEABLE_COLUMNS.map(c => c.id);
      expect(ids).toEqual(['description', 'createdAt']);
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

    it('should not hide description by default', () => {
      expect(DEFAULT_COLUMN_VISIBILITY['description']).toBeUndefined();
    });
  });
});

describe('PartyTableComponent', () => {
  let fixture: ComponentFixture<PartyTableComponent>;
  let component: PartyTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(PartyTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('parties', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should define all expected columns', () => {
    fixture.detectChanges();
    const columnIds = component['_columns'].map(c => c.id);
    expect(columnIds).toEqual(['select', 'name', 'description', 'documentCount', 'createdAt']);
  });

  it('should map name column to Name sort key via sort input', () => {
    fixture.componentRef.setInput('sortBy', 'Name');
    fixture.componentRef.setInput('sortDir', 'Asc');
    fixture.detectChanges();

    const sorting = component['_sorting']();
    expect(sorting).toEqual([{ id: 'name', desc: false }]);
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
    fixture.componentRef.setInput('columnVisibility', { createdAt: false, description: true });
    fixture.detectChanges();

    const vis = component['_columnVisibility']();
    expect(vis['createdAt']).toBe(false);
    expect(vis['description']).toBe(true);
  });

  it('should sync selected IDs input to row selection state', () => {
    fixture.componentRef.setInput('parties', [
      { id: 'p1', name: 'A', documentCount: 0, createdAt: new Date().toISOString() },
      { id: 'p2', name: 'B', documentCount: 0, createdAt: new Date().toISOString() },
    ]);
    fixture.componentRef.setInput('selectedIds', new Set(['p1']));
    fixture.detectChanges();

    const selection = component['_rowSelection']();
    expect(selection['p1']).toBe(true);
    expect(selection['p2']).toBeUndefined();
  });

  it('should emit sortChange when sorting changes via table', () => {
    fixture.componentRef.setInput('parties', []);
    fixture.detectChanges();

    let emitted: { sortBy: string; sortDir: string } | null | undefined;
    component.sortChange.subscribe(val => (emitted = val));

    // Simulate TanStack sorting change via the onSortingChange callback
    const tableOptions = component['table'].options;
    const onSortingChange = tableOptions.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => [{ id: 'name', desc: false }]);

    expect(emitted).toEqual({ sortBy: 'Name', sortDir: 'Asc' });
  });

  it('should emit null sortChange when sorting is cleared', () => {
    fixture.componentRef.setInput('parties', []);
    fixture.detectChanges();

    let emitted: { sortBy: string; sortDir: string } | null | undefined;
    component.sortChange.subscribe(val => (emitted = val));

    const tableOptions = component['table'].options;
    const onSortingChange = tableOptions.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => []);

    expect(emitted).toBeNull();
  });

  it('should emit selectionChange when row selection changes via table', () => {
    fixture.componentRef.setInput('parties', [
      { id: 'p1', name: 'A', documentCount: 0, createdAt: new Date().toISOString() },
    ]);
    fixture.detectChanges();

    let emitted: Set<string> | undefined;
    component.selectionChange.subscribe(val => (emitted = val));

    const tableOptions = component['table'].options;
    const onRowSelectionChange = tableOptions.onRowSelectionChange!;
    (onRowSelectionChange as (updater: unknown) => void)(() => ({ p1: true }));

    expect(emitted).toEqual(new Set(['p1']));
  });
});
