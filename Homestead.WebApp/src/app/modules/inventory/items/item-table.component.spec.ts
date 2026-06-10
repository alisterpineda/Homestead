import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ItemTableComponent, formatPath, type ItemSortState } from './item-table.component';
import type { ItemResponse } from '@/api/generated/models/item-response';

function makeItem(overrides: Partial<ItemResponse> = {}): ItemResponse {
  return {
    id: 'item-1',
    name: 'Cordless drill',
    fungibility: 'NonFungible',
    status: 'Active',
    isContainer: false,
    path: [],
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

  describe('location cell', () => {
    const col = () => component['_columns']().find(c => c.id === 'location')!;

    it('renders the resolved path joined with ›', () => {
      const item = makeItem({
        resolvedAreaId: 'garage',
        path: [
          { id: 'garage', name: 'Garage', kind: 'Area' },
          { id: 'cupboard', name: 'Cupboard', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      });
      const html = renderCell(col().cell, item);
      expect(html).toContain('Garage › Cupboard › Bin');
      expect(html).toContain('title="Garage › Cupboard › Bin"');
    });

    it('prepends an italic Unplaced for a chain that resolves to Unplaced', () => {
      const item = makeItem({
        resolvedAreaId: null,
        path: [
          { id: 'cupboard', name: 'Cupboard', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      });
      const html = renderCell(col().cell, item);
      expect(html).toContain('Unplaced');
      expect(html).toContain('italic');
      expect(html).toContain('Cupboard › Bin');
    });

    it('renders an italic Unplaced label when the item is directly unplaced', () => {
      const html = renderCell(col().cell, makeItem({ path: [] }));
      expect(html).toContain('Unplaced');
      expect(html).toContain('italic');
    });
  });

  describe('formatPath', () => {
    it('joins names and prefixes Unplaced for an unplaced-rooted chain', () => {
      expect(formatPath(makeItem({ path: [] }))).toBe('Unplaced');
      expect(formatPath(makeItem({
        resolvedAreaId: 'g', path: [{ id: 'g', name: 'Garage', kind: 'Area' }],
      }))).toBe('Garage');
      expect(formatPath(makeItem({
        resolvedAreaId: null, path: [{ id: 'c', name: 'Cupboard', kind: 'Container' }],
      }))).toBe('Unplaced › Cupboard');
    });
  });

  describe('name cell (DOM render)', () => {
    it('renders the name, serial subtitle, and a container icon for container rows', () => {
      fixture.componentRef.setInput('items', [makeItem({ name: 'Camera', serial: 'SN-9', isContainer: true })]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).toContain('Camera');
      expect(host.textContent).toContain('SN-9');
      expect(host.querySelector('app-item-name-cell ng-icon')).toBeTruthy();
    });

    it('omits the container icon for non-container rows', () => {
      fixture.componentRef.setInput('items', [makeItem({ isContainer: false })]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('app-item-name-cell ng-icon')).toBeNull();
    });
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
    let viewed: ItemResponse | undefined;
    let archived: ItemResponse | undefined;
    let deleted: ItemResponse | undefined;
    component.edit.subscribe(v => (edited = v));
    component.viewContents.subscribe(v => (viewed = v));
    component.archiveRestore.subscribe(v => (archived = v));
    component.delete.subscribe(v => (deleted = v));

    const meta = component['_meta'];
    meta.onEdit(item);
    meta.onViewContents(item);
    meta.onArchiveRestore(item);
    meta.onDelete(item);

    expect(edited).toBe(item);
    expect(viewed).toBe(item);
    expect(archived).toBe(item);
    expect(deleted).toBe(item);
  });
});
