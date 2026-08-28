import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { InventoryCardGridComponent } from './inventory-card-grid.component';
import { InventoryRowActionsComponent } from './inventory-row-actions.component';
import { durableToRow, consumableToRow, placeConsumableToRow, type InventoryRow } from './inventory-row';
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

describe('InventoryCardGridComponent', () => {
  let fixture: ComponentFixture<InventoryCardGridComponent>;
  let component: InventoryCardGridComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideRouter([]),
      ],
    });
    fixture = TestBed.createComponent(InventoryCardGridComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('rows', []);
  });

  function setup(rows: InventoryRow[]): HTMLElement {
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function cards(host: HTMLElement): HTMLElement[] {
    return Array.from(host.querySelectorAll<HTMLElement>('[role="button"][hlmCard]'));
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('name line', () => {
    it('renders one card per row, each with its name', () => {
      const host = setup([
        durableToRow(makeItem({ id: 'a', name: 'Cordless drill' })),
        consumableToRow(makeConsumable({ id: 'b', name: 'Paper towels' })),
      ]);
      expect(cards(host)).toHaveLength(2);
      expect(host.textContent).toContain('Cordless drill');
      expect(host.textContent).toContain('Paper towels');
    });

    it('renders the serial as a sub-line when the durable has one, and nothing when it does not', () => {
      const withSerial = setup([durableToRow(makeItem({ name: 'Camera', serial: 'SN-9' }))]);
      expect(withSerial.textContent).toContain('SN-9');

      const without = setup([durableToRow(makeItem({ name: 'Camera' }))]);
      // Name only — no second span in the title block.
      const titleBlock = without.querySelector('.min-w-0.flex-1')!;
      expect(titleBlock.querySelectorAll('span')).toHaveLength(1);
      expect(titleBlock.textContent).toContain('Camera');
    });
  });

  describe('status', () => {
    it('shows a badge for a non-default status', () => {
      const host = setup([durableToRow(makeItem({ status: 'Lent' }))]);
      const badge = host.querySelector('app-item-status-badge')!;
      expect(badge.textContent?.trim()).toBe('Lent');
    });

    // Cards do not opt into `showDefault` — unlike the dense table column, an Active chip on every
    // card would be pure noise.
    it('renders no badge for the resting Active status', () => {
      const host = setup([durableToRow(makeItem({ status: 'Active' }))]);
      const badge = host.querySelector('app-item-status-badge')!;
      expect(badge.textContent?.trim()).toBe('');
      expect(badge.classList).toContain('hidden');
    });
  });

  it('renders the lent-to line only when the durable records a borrower', () => {
    const lent = setup([durableToRow(makeItem({ status: 'Lent', lentTo: 'Dave' }))]);
    expect(lent.textContent).toContain('Lent to Dave');

    const plain = setup([durableToRow(makeItem({ status: 'Active' }))]);
    expect(plain.textContent).not.toContain('Lent to');
  });

  describe('footer', () => {
    it('renders quantity, unit, hint and the Low badge for a consumable', () => {
      const consumable = makeConsumable({ totalOnHand: 10, unit: 'rolls', isLow: true });
      const host = setup([placeConsumableToRow({ consumable, localQuantity: 2.5 })]);
      const text = host.textContent ?? '';
      expect(text).toContain('2.5');
      expect(text).toContain('rolls');
      expect(text).toContain('of 10 total');
      expect(text).toContain('Low');
    });

    it('omits the Low badge for a healthy consumable', () => {
      const host = setup([consumableToRow(makeConsumable({ isLow: false }))]);
      expect(host.textContent).not.toContain('Low');
    });

    it('gives a durable card an age and nothing else in the footer', () => {
      const host = setup([durableToRow(makeItem({ name: 'Camera' }))]);
      const footer = host.querySelector('[role="button"] > div:last-child')!;
      expect(footer.textContent?.trim()).toBe('just now');
      expect(host.querySelector('.tabular-nums.ml-auto, .ml-auto.tabular-nums')).toBeTruthy();
    });

    it('emphasises the quantity on a live row but not on a terminal one', () => {
      const live = setup([consumableToRow(makeConsumable({ status: 'Active' }))]);
      expect(live.querySelector('.tabular-nums')!.classList).toContain('text-foreground');

      const archived = setup([consumableToRow(makeConsumable({ status: 'Archived' }))]);
      expect(archived.querySelector('.tabular-nums')!.classList).not.toContain('text-foreground');
    });
  });

  describe('location line', () => {
    function locationLine(host: HTMLElement): HTMLElement | null {
      return host.querySelector<HTMLElement>('[role="button"] ng-icon[name="lucideMapPin"]')
        ?.parentElement ?? null;
    }

    it('renders a resolved path as crumbs whose tooltip is the full path', () => {
      const host = setup([durableToRow(makeItem({
        resolvedAreaId: 'house',
        path: [
          { id: 'house', name: 'House', kind: 'Area' },
          { id: 'basement', name: 'Basement', kind: 'Area' },
          { id: 'chest', name: 'Chest', kind: 'Container' },
          { id: 'bin', name: 'Bin', kind: 'Container' },
        ],
      }))]);
      const path = host.querySelector('app-location-path')!;
      // Collapsed on screen (the card is narrow by construction)...
      expect(path.textContent).toContain('House');
      expect(path.textContent).toContain('Bin');
      expect(path.textContent).not.toContain('Chest');
      // ...but the full path is always reachable via the tooltip.
      expect(path.getAttribute('title')).toBe('House › Basement › Chest › Bin');
    });

    it('renders an italic placeholder for an unplaced durable', () => {
      const host = setup([durableToRow(makeItem({ path: [] }))]);
      expect(host.querySelector('app-location-path')).toBeNull();
      const placeholder = locationLine(host)!.querySelector('span')!;
      expect(placeholder.textContent?.trim()).toBe('Unplaced');
      expect(placeholder.classList).toContain('italic');
      expect(placeholder.classList).toContain('text-muted-foreground');
    });

    it('renders the flat summary text for a consumable spread across locations', () => {
      const host = setup([consumableToRow(makeConsumable({
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
      }))]);
      const summary = locationLine(host)!.querySelector('span')!;
      expect(summary.textContent?.trim()).toBe('2 locations');
      expect(summary.classList).toContain('truncate');
      expect(summary.getAttribute('title')).toBe('2 locations');
    });

    it('hides the location line entirely when the location column is turned off', () => {
      const host = setup([durableToRow(makeItem({
        resolvedAreaId: 'garage',
        path: [{ id: 'garage', name: 'Garage', kind: 'Area' }],
      }))]);
      expect(locationLine(host)).not.toBeNull();

      fixture.componentRef.setInput('columnVisibility', { location: false });
      fixture.detectChanges();

      expect(locationLine(host)).toBeNull();
      expect(host.textContent).not.toContain('Garage');
    });
  });

  it('mutes terminal-status cards and leaves live ones alone', () => {
    const host = setup([
      durableToRow(makeItem({ id: 'sold', name: 'Sold thing', status: 'Sold' })),
      durableToRow(makeItem({ id: 'live', name: 'Live thing', status: 'Active' })),
    ]);
    const [sold, live] = cards(host);
    expect(sold.classList).toContain('italic');
    expect(sold.classList).toContain('text-muted-foreground');
    expect(live.classList).not.toContain('italic');
    expect(live.classList).toContain('cursor-pointer');
    // hlmCard applies its own padding/gap through the `classes()` helper; the card's own layout
    // classes have to win the tailwind-merge, or every card renders at the default card spacing.
    expect(live.classList).toContain('p-3');
    expect(live.classList).toContain('gap-1.5');
    expect(live.className).not.toMatch(/\bpy-\(/);
  });

  describe('opening a card', () => {
    it('emits open on click, Enter and Space', () => {
      const row = durableToRow(makeItem({ id: 'clicked', name: 'Camera' }));
      const host = setup([row]);
      const opened: InventoryRow[] = [];
      component.open.subscribe(v => opened.push(v));

      const card = cards(host)[0];
      expect(card.getAttribute('tabindex')).toBe('0');
      card.click();
      card.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      card.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

      expect(opened).toEqual([row, row, row]);
    });

    it('does not emit open when the row-actions trigger is clicked', async () => {
      const host = setup([durableToRow(makeItem({ id: 'clicked' }))]);
      let opened: InventoryRow | undefined;
      component.open.subscribe(v => (opened = v));

      const trigger = host.querySelector<HTMLButtonElement>('app-inventory-row-actions button')!;
      trigger.click();
      // The dropdown trigger schedules a 0-delay timeout on open that touches the overlay —
      // let it run before the fixture is torn down so it doesn't surface as an unhandled error.
      await new Promise(resolve => setTimeout(resolve));

      expect(opened).toBeUndefined();
    });

    it('does not emit open when Enter or Space is pressed on the row-actions trigger', () => {
      const host = setup([durableToRow(makeItem({ id: 'keyed' }))]);
      const opened: InventoryRow[] = [];
      component.open.subscribe(v => opened.push(v));

      const trigger = host.querySelector<HTMLButtonElement>('app-inventory-row-actions button')!;
      const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
      const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
      trigger.dispatchEvent(enter);
      trigger.dispatchEvent(space);

      expect(opened).toEqual([]);
      // The card's Space handler must not have cancelled the button's native activation either —
      // CdkMenuTrigger opens on the click the browser synthesises from that keystroke.
      expect(space.defaultPrevented).toBe(false);
    });
  });

  it('re-emits every row-action output with the row', () => {
    const row = consumableToRow(makeConsumable());
    const host = setup([row]);
    const actions = fixture.debugElement.query(
      el => el.nativeElement === host.querySelector('app-inventory-row-actions'),
    ).componentInstance as InventoryRowActionsComponent;

    const seen: Record<string, InventoryRow | undefined> = {};
    component.edit.subscribe(v => (seen['edit'] = v));
    component.viewContents.subscribe(v => (seen['viewContents'] = v));
    component.viewDetails.subscribe(v => (seen['viewDetails'] = v));
    component.archiveRestore.subscribe(v => (seen['archiveRestore'] = v));
    component.delete.subscribe(v => (seen['delete'] = v));

    actions.edit.emit(row);
    actions.viewContents.emit(row);
    actions.viewDetails.emit(row);
    actions.archiveRestore.emit(row);
    actions.delete.emit(row);

    expect(seen).toEqual({
      edit: row,
      viewContents: row,
      viewDetails: row,
      archiveRestore: row,
      delete: row,
    });
  });
});
