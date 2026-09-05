import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { InventoryWarningPanelsComponent, type WarningPanelsScope } from './inventory-warning-panels.component';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

const ALL_QUIET: GetInventoryOverviewResponse = {
  expiringWarrantyCount: 0,
  expiringWarranties: [],
  lowStockCount: 0,
  lowStockConsumables: [],
  lentCount: 0,
  lentDurables: [],
  unplacedCount: 0,
  areas: [],
  recentlyUpdated: [],
};

const BUSY: GetInventoryOverviewResponse = {
  expiringWarrantyCount: 0,
  expiringWarranties: [],
  lowStockCount: 7,
  lowStockConsumables: [
    { id: 'glue', name: 'Glue', totalOnHand: 0, unit: 'tubes', restockThreshold: 5 },
    { id: 'tape', name: 'Tape', totalOnHand: 2, unit: 'rolls', restockThreshold: 3 },
  ],
  // Lent is now folded into needs-attention as an alert tile; unplaced is still a location bucket
  // (a place card), never an alert — the warning panels must ignore it.
  lentCount: 1,
  lentDurables: [{ id: 'ladder', name: 'Ladder', statusNote: 'Dave next door' }],
  unplacedCount: 2,
  areas: [],
  recentlyUpdated: [],
};

describe('InventoryWarningPanelsComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
      ],
    });
  });

  async function create(scope: WarningPanelsScope): Promise<ComponentFixture<InventoryWarningPanelsComponent>> {
    const fixture = TestBed.createComponent(InventoryWarningPanelsComponent);
    fixture.componentRef.setInput('scope', scope);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<InventoryWarningPanelsComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function links(fixture: ComponentFixture<InventoryWarningPanelsComponent>): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  function trigger(fixture: ComponentFixture<InventoryWarningPanelsComponent>): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button[hlmCollapsibleTrigger]');
  }

  /** The preview row whose text contains the consumable name — used to scope per-row badge assertions. */
  function row(fixture: ComponentFixture<InventoryWarningPanelsComponent>, name: string): HTMLElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('li'),
    ).find(li => li.textContent?.includes(name));
  }

  // ----- Root mode -----

  it('renders both the low-stock and lent-out tiles (unplaced is not an alert), collapsed by default', async () => {
    const fixture = await create({ kind: 'root', overview: BUSY });

    const content = text(fixture);
    expect(content).toContain('Low stock');
    expect(content).toContain('Glue');
    expect(content).toContain('0 tubes / 5');
    // Lent is now folded into needs-attention as its own tile, with the to-whom note.
    expect(content).toContain('Lent out');
    expect(content).toContain('Ladder');
    expect(content).toContain('to Dave next door');
    // Unplaced is still a location bucket, never an alert.
    expect(content).not.toContain('Unplaced');
    // 7 low-stock total with 2 previewed → "+5 more".
    expect(content).toContain('+5 more');

    // Both slices deep-link into the items list; no unplaced link.
    expect(links(fixture)).toEqual([
      '/inventory/items?lowStock=true',
      '/inventory/items?lent=true',
    ]);

    // Collapsed by default in every scope — incl. the root — so the panels don't fight the page.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('false');
  });

  it('flags a zero-on-hand consumable "Out" but not a merely-low one', async () => {
    const fixture = await create({ kind: 'root', overview: BUSY });

    // Glue is at 0 on hand → "Out"; Tape is at 2 → just low, no badge.
    expect(row(fixture, 'Glue')?.textContent).toContain('Out');
    expect(row(fixture, 'Tape')?.textContent).not.toContain('Out');

    // The badge carries an accessible name (matching the module's destructive-badge convention).
    const badge = row(fixture, 'Glue')?.querySelector('[aria-label="Out of stock"]');
    expect(badge?.textContent?.trim()).toBe('Out');
    expect(row(fixture, 'Tape')?.querySelector('[aria-label="Out of stock"]')).toBeNull();
  });

  it('renders the warranty-expiring tile with a soonest-first preview and a ?warranty=expiring link', async () => {
    const fixture = await create({
      kind: 'root',
      overview: {
        ...BUSY,
        expiringWarrantyCount: 4,
        expiringWarranties: [
          { id: 'drill', name: 'Cordless Drill', expiresOn: '2026-09-20' },
          { id: 'fridge', name: 'Fridge', expiresOn: '2026-10-01' },
        ],
      },
    });

    const content = text(fixture);
    expect(content).toContain('Warranty expiring');
    expect(content).toContain('Cordless Drill');
    expect(content).toContain('until Sep 20, 2026');
    // 4 expiring with 2 previewed → "+2 more".
    expect(content).toContain('+2 more');
    expect(links(fixture)).toContain('/inventory/items?warranty=expiring');
  });

  it('opens the panel on an expiring warranty alone, and counts it in the collapsed summary', async () => {
    const fixture = await create({
      kind: 'root',
      overview: {
        ...ALL_QUIET,
        expiringWarrantyCount: 2,
        expiringWarranties: [{ id: 'drill', name: 'Cordless Drill', expiresOn: '2026-09-20' }],
      },
    });

    const content = text(fixture);
    expect(content).toContain('Warranty expiring');
    expect(content).not.toContain('Low stock');
    expect(content).not.toContain('Lent out');
    expect(content).toContain('2 expiring');
    expect(links(fixture)).toEqual(['/inventory/items?warranty=expiring']);
  });

  it('never shows a warranty tile in a drilled place (root-scope alert only)', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 3, lentCount: 0 });
    expect(text(fixture)).not.toContain('Warranty expiring');
  });

  it('renders nothing at the root when nothing is running low', async () => {
    const fixture = await create({ kind: 'root', overview: ALL_QUIET });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });

  it('renders at the root with just the lent tile when only lent is nonzero', async () => {
    const fixture = await create({
      kind: 'root',
      overview: { ...ALL_QUIET, lentCount: 3, lentDurables: [{ id: 'ladder', name: 'Ladder', statusNote: 'Dave next door' }] },
    });

    const content = text(fixture);
    // Lent now opens the needs-attention panel on its own — even with nothing low.
    expect(content).toContain('Lent out');
    expect(content).toContain('Ladder');
    expect(content).not.toContain('Low stock');
    expect(links(fixture)).toEqual(['/inventory/items?lent=true']);
  });

  it('stays hidden at the root when only unplaced is nonzero (unplaced is not an alert)', async () => {
    const fixture = await create({
      kind: 'root',
      overview: { ...ALL_QUIET, unplacedCount: 4 },
    });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });

  // ----- Place mode -----

  it('renders the area-scoped low-stock panel, collapsed by default, no lent panel when nothing lent', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 3, lentCount: 0 });

    const content = text(fixture);
    expect(content).toContain('Low consumables stored here');
    // Nothing lent here → no lent tile; unplaced never applies to a place.
    expect(content).not.toContain('Items lent from here');
    expect(content).not.toContain('Unplaced');

    // The View-all link carries the area filter so it composes with the items-list deep-link reader.
    expect(links(fixture)).toEqual(['/inventory/items?area=garage&lowStock=true']);

    // Drilled re-seeds collapsed so the panel doesn't fight the place browser for vertical space.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('false');
  });

  it('renders a count-only low-stock panel (no link) when drilled into a container', async () => {
    // A container drill carries null params — the list can't scope to a container, so the panel is
    // count-only (the matching items already render in the contents table below).
    const fixture = await create({ kind: 'place', viewAllParams: null, lowStockCount: 1, lentCount: 0 });

    expect(text(fixture)).toContain('Low consumables stored here');
    expect(links(fixture)).toEqual([]);
  });

  it('renders a count-only "Items lent from here" panel (no link) when the place has lent items', async () => {
    // Lent in a drilled place is always count-only — Browse already scopes spatially and the lent
    // items show in the contents table below.
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 0, lentCount: 2 });

    const content = text(fixture);
    expect(content).toContain('Items lent from here');
    expect(content).not.toContain('Low consumables stored here');
    expect(links(fixture)).toEqual([]);
  });

  it('renders nothing in a drilled place with no low stock and nothing lent', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 0, lentCount: 0 });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });
});
