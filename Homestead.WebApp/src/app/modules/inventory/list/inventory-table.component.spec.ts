import { TestBed, ComponentFixture } from '@angular/core/testing';
import { InventoryTableComponent, type InventorySortState } from './inventory-table.component';
import { durableToRow, consumableToRow, consumableToDrillRow, formatPath, type InventoryRow } from './inventory-row';
import type { DurableResponse } from '@/api/generated/models/durable-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';

function makeItem(overrides: Partial<DurableResponse> = {}): DurableResponse {
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

function makeConsumable(overrides: Partial<ConsumableResponse> = {}): ConsumableResponse {
  return {
    id: 'consumable-1',
    name: 'Paper towels',
    status: 'Active',
    unit: 'rolls',
    totalOnHand: 12,
    isLow: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    stockLocations: [],
    ...overrides,
  };
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function renderCell(cell: unknown, row: InventoryRow): string {
  return (cell as (info: any) => string)({ row: { original: row }, getValue: () => undefined });
}

describe('InventoryTableComponent', () => {
  let fixture: ComponentFixture<InventoryTableComponent>;
  let component: InventoryTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(InventoryTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('rows', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('defines the expected columns', () => {
    const ids = component['_columns']().map(c => c.id);
    expect(ids).toEqual(['name', 'status', 'location', 'onHand', 'updatedAt', 'actions']);
  });

  describe('location cell', () => {
    const col = () => component['_columns']().find(c => c.id === 'location')!;

    it('renders an item resolved path joined with ›', () => {
      const row = durableToRow(makeItem({
        resolvedAreaId: 'garage',
        path: [
          { id: 'garage', name: 'Garage', kind: 'Area' },
          { id: 'cupboard', name: 'Cupboard', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      }));
      const html = renderCell(col().cell, row);
      expect(html).toContain('Garage › Cupboard › Bin');
      expect(html).toContain('title="Garage › Cupboard › Bin"');
    });

    it('prepends an italic Unplaced for an item chain that resolves to Unplaced', () => {
      const row = durableToRow(makeItem({
        resolvedAreaId: null,
        path: [
          { id: 'cupboard', name: 'Cupboard', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      }));
      const html = renderCell(col().cell, row);
      expect(html).toContain('Unplaced');
      expect(html).toContain('italic');
      expect(html).toContain('Cupboard › Bin');
    });

    it('renders an italic Unplaced label when the item is directly unplaced', () => {
      const html = renderCell(col().cell, durableToRow(makeItem({ path: [] })));
      expect(html).toContain('Unplaced');
      expect(html).toContain('italic');
    });

    it('renders a muted dash for a consumable with no stock', () => {
      const html = renderCell(col().cell, consumableToRow(makeConsumable({ totalOnHand: 0 })));
      expect(html).toContain('—');
      expect(html).toContain('italic');
    });

    it('renders the single location label for a consumable stocked in one place', () => {
      const row = consumableToRow(makeConsumable({
        stockLocations: [{
          id: 'e1', quantity: 12, areaId: 'garage',
          path: [{ id: 'garage', name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
        }],
      }));
      expect(renderCell(col().cell, row)).toContain('Garage');
    });

    it('summarizes a consumable spread across locations as a count', () => {
      const row = consumableToRow(makeConsumable({
        stockLocations: [
          {
            id: 'e1', quantity: 8, areaId: 'garage',
            path: [{ id: 'garage', name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
          },
          {
            id: 'e2', quantity: 4, areaId: 'pantry',
            path: [{ id: 'pantry', name: 'Pantry', kind: 'Area' }], locationLabel: 'Pantry',
          },
        ],
      }));
      expect(renderCell(col().cell, row)).toContain('2 locations');
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
      fixture.componentRef.setInput('rows', [durableToRow(makeItem({ name: 'Camera', serial: 'SN-9', isContainer: true }))]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).toContain('Camera');
      expect(host.textContent).toContain('SN-9');
      expect(host.querySelector('app-inventory-name-cell ng-icon')).toBeTruthy();
    });

    it('omits the icon for plain (non-container) item rows', () => {
      fixture.componentRef.setInput('rows', [durableToRow(makeItem({ isContainer: false }))]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('app-inventory-name-cell ng-icon')).toBeNull();
    });

    it('renders a consumable icon for consumable rows', () => {
      fixture.componentRef.setInput('rows', [consumableToRow(makeConsumable())]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      const icon = host.querySelector('app-inventory-name-cell ng-icon');
      expect(icon).toBeTruthy();
      expect(icon!.getAttribute('aria-label')).toBe('Consumable');
    });
  });

  describe('on hand cell (DOM render)', () => {
    it('renders quantity, unit, and a Low badge for a low consumable', () => {
      fixture.componentRef.setInput('rows', [consumableToRow(makeConsumable({ totalOnHand: 2.5, isLow: true }))]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).toContain('2.5');
      expect(host.textContent).toContain('rolls');
      expect(host.textContent).toContain('Low');
    });

    it('renders a dash for item rows', () => {
      fixture.componentRef.setInput('rows', [durableToRow(makeItem())]);
      fixture.detectChanges();
      const cell = fixture.nativeElement.querySelector('app-inventory-onhand-cell') as HTMLElement;
      expect(cell.textContent).toContain('—');
      expect(cell.textContent).not.toContain('Low');
    });

    it('renders the local quantity with the of-total hint for a drilled consumable row', () => {
      const consumable = makeConsumable({
        totalOnHand: 10,
        stockLocations: [
          {
            id: 'e1', quantity: 4, containerItemId: 'chest',
            path: [
              { id: 'garage', name: 'Garage', kind: 'Area' },
              { id: 'chest', name: 'Tool chest', kind: 'Container' },
            ],
            locationLabel: 'Garage › Tool chest',
          },
          {
            id: 'e2', quantity: 6, areaId: 'garage',
            path: [{ id: 'garage', name: 'Garage', kind: 'Area' }], locationLabel: 'Garage',
          },
        ],
      });
      fixture.componentRef.setInput('rows', [consumableToDrillRow(consumable, 'chest')!]);
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).toContain('4');
      expect(host.textContent).toContain('of 10 total');
    });
  });

  it('marks terminal-status rows as muted/italic and all rows as clickable', () => {
    const sold = durableToRow(makeItem({ status: 'Sold' }));
    const archivedConsumable = consumableToRow(makeConsumable({ status: 'Archived' }));
    const active = durableToRow(makeItem({ status: 'Active' }));
    expect(component['getRowClass'](sold)).toContain('italic');
    expect(component['getRowClass'](archivedConsumable)).toContain('italic');
    expect(component['getRowClass'](active)).not.toContain('italic');
    expect(component['getRowClass'](active)).toContain('cursor-pointer');
    expect(component['getRowClass'](sold)).toContain('cursor-pointer');
  });

  describe('row click', () => {
    it('emits open with the row when a body row is clicked', () => {
      const row = durableToRow(makeItem({ id: 'clicked', name: 'Camera' }));
      fixture.componentRef.setInput('rows', [row]);
      fixture.detectChanges();

      let opened: InventoryRow | undefined;
      component.open.subscribe(v => (opened = v));

      const tr = fixture.nativeElement.querySelector('tbody tr') as HTMLTableRowElement;
      tr.click();

      expect(opened).toBe(row);
    });

    it('emits open via keyboard (Enter and Space) on a focused row', () => {
      const row = consumableToRow(makeConsumable({ id: 'kbd' }));
      fixture.componentRef.setInput('rows', [row]);
      fixture.detectChanges();

      const opened: InventoryRow[] = [];
      component.open.subscribe(v => opened.push(v));

      const tr = fixture.nativeElement.querySelector('tbody tr') as HTMLTableRowElement;
      expect(tr.getAttribute('tabindex')).toBe('0');
      tr.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      tr.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

      expect(opened).toEqual([row, row]);
    });

    it('does not emit open when the row-actions trigger is clicked', async () => {
      const row = durableToRow(makeItem({ id: 'clicked' }));
      fixture.componentRef.setInput('rows', [row]);
      fixture.detectChanges();

      let opened: InventoryRow | undefined;
      component.open.subscribe(v => (opened = v));

      const trigger = fixture.nativeElement.querySelector(
        'tbody tr app-inventory-row-actions button',
      ) as HTMLButtonElement;
      trigger.click();
      // The dropdown trigger schedules a 0-delay timeout on open that touches the overlay —
      // let it run before the fixture is torn down so it doesn't surface as an unhandled error.
      await new Promise(resolve => setTimeout(resolve));

      expect(opened).toBeUndefined();
    });
  });

  it('emits sortChange when sorting changes via the table', () => {
    fixture.detectChanges();
    let emitted: InventorySortState | undefined;
    component.sortChange.subscribe(v => (emitted = v));

    const onSortingChange = component['table'].options.onSortingChange!;
    (onSortingChange as (updater: unknown) => void)(() => [{ id: 'status', desc: true }]);

    expect(emitted).toEqual({ id: 'status', desc: true });
  });

  it('emits row-action outputs through the table meta', () => {
    const row = consumableToRow(makeConsumable());
    let edited: InventoryRow | undefined;
    let viewedContents: InventoryRow | undefined;
    let viewedDetails: InventoryRow | undefined;
    let archived: InventoryRow | undefined;
    let deleted: InventoryRow | undefined;
    component.edit.subscribe(v => (edited = v));
    component.viewContents.subscribe(v => (viewedContents = v));
    component.viewDetails.subscribe(v => (viewedDetails = v));
    component.archiveRestore.subscribe(v => (archived = v));
    component.delete.subscribe(v => (deleted = v));

    const meta = component['_meta'];
    meta.onEdit(row);
    meta.onViewContents(row);
    meta.onViewDetails(row);
    meta.onArchiveRestore(row);
    meta.onDelete(row);

    expect(edited).toBe(row);
    expect(viewedContents).toBe(row);
    expect(viewedDetails).toBe(row);
    expect(archived).toBe(row);
    expect(deleted).toBe(row);
  });
});
