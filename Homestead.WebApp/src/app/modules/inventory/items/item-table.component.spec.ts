import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ItemTableComponent, type ItemSortState } from './item-table.component';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { ItemStatus } from '@/api/generated/models/item-status';

function makeItem(overrides: Partial<ItemResponse> = {}): ItemResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function renderCell(cell: unknown, item: ItemResponse): string {
  return (cell as (info: any) => string)({ row: { original: item }, getValue: () => undefined });
}

describe('ItemTableComponent', () => {
  let fixture: ComponentFixture<ItemTableComponent>;
  let component: ItemTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(ItemTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('items', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('defines the expected columns', () => {
    const ids = component['_columns']().map(c => c.id);
    expect(ids).toEqual(['name', 'status', 'location', 'acquiredOn', 'updatedAt', 'actions']);
  });

  it('renders the area name in the location cell', () => {
    const col = component['_columns']().find(c => c.id === 'location')!;
    const html = renderCell(col.cell, makeItem({ areaName: 'Garage' }));
    expect(html).toContain('Garage');
    expect(html).not.toContain('Unplaced');
  });

  it('renders an italic Unplaced label when the item has no area', () => {
    const col = component['_columns']().find(c => c.id === 'location')!;
    const html = renderCell(col.cell, makeItem({ areaName: null }));
    expect(html).toContain('Unplaced');
    expect(html).toContain('italic');
  });

  it('renders the serial as a subtitle in the name cell', () => {
    const col = component['_columns']().find(c => c.id === 'name')!;
    const html = renderCell(col.cell, makeItem({ name: 'Camera', serial: 'SN-9' }));
    expect(html).toContain('Camera');
    expect(html).toContain('SN-9');
  });

  it('marks terminal-status rows as muted/italic', () => {
    expect(component['getRowClass'](makeItem({ status: 'Sold' }))).toContain('italic');
    expect(component['getRowClass'](makeItem({ status: 'Archived' }))).toContain('italic');
    expect(component['getRowClass'](makeItem({ status: 'Active' }))).toBe('');
  });

  it('emits sortChange when sorting changes via the table', () => {
    fixture.detectChanges();
    let emitted: ItemSortState | undefined;
    component.sortChange.subscribe(v => (emitted = v));

    const onSortingChange = component['table'].options.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => [{ id: 'status', desc: true }]);

    expect(emitted).toEqual({ id: 'status', desc: true });
  });

  it('emits row-action outputs through the table meta', () => {
    const item = makeItem();
    let edited: ItemResponse | undefined;
    let archived: ItemResponse | undefined;
    let deleted: ItemResponse | undefined;
    component.edit.subscribe(v => (edited = v));
    component.archiveRestore.subscribe(v => (archived = v));
    component.delete.subscribe(v => (deleted = v));

    const meta = component['_meta'];
    meta.onEdit(item);
    meta.onArchiveRestore(item);
    meta.onDelete(item);

    expect(edited).toBe(item);
    expect(archived).toBe(item);
    expect(deleted).toBe(item);
  });
});
