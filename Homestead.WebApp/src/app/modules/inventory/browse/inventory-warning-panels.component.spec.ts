import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { InventoryWarningPanelsComponent, type WarningPanelsScope } from './inventory-warning-panels.component';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

const ALL_QUIET: GetInventoryOverviewResponse = {
  lowStockCount: 0,
  lowStockSupplies: [],
  lentCount: 0,
  lentItems: [],
  unplacedCount: 0,
  unplacedItems: [],
  areas: [],
};

const BUSY: GetInventoryOverviewResponse = {
  lowStockCount: 7,
  lowStockSupplies: [
    { id: 'glue', name: 'Glue', totalOnHand: 0, unit: 'tubes', restockThreshold: 5 },
    { id: 'tape', name: 'Tape', totalOnHand: 2, unit: 'rolls', restockThreshold: 3 },
  ],
  // Lent / unplaced are now neutral statuses (see InventoryStatusChipsComponent), not alerts —
  // the warning panels must ignore them entirely.
  lentCount: 1,
  lentItems: [{ id: 'ladder', name: 'Ladder', lentTo: 'Dave next door' }],
  unplacedCount: 2,
  unplacedItems: [
    { id: 'wrench', name: 'Adrift wrench' },
    { id: 'bin', name: 'Floating bin' },
  ],
  areas: [],
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

  /** The preview row whose text contains the supply name — used to scope per-row badge assertions. */
  function row(fixture: ComponentFixture<InventoryWarningPanelsComponent>, name: string): HTMLElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('li'),
    ).find(li => li.textContent?.includes(name));
  }

  // ----- Root mode -----

  it('renders only the low-stock tile (lent/unplaced are statuses, not alerts), open by default', async () => {
    const fixture = await create({ kind: 'root', overview: BUSY });

    const content = text(fixture);
    expect(content).toContain('Low stock');
    expect(content).toContain('Glue');
    expect(content).toContain('0 tubes / 5');
    // Lent / unplaced no longer live in the alert frame.
    expect(content).not.toContain('Lent out');
    expect(content).not.toContain('Unplaced');
    expect(content).not.toContain('Ladder');
    expect(content).not.toContain('Adrift wrench');
    // 7 low-stock total with 2 previewed → "+5 more".
    expect(content).toContain('+5 more');

    // The only deep-link is the low-stock View-all — no lent/unplaced links.
    expect(links(fixture)).toEqual(['/inventory/items?lowStock=true']);

    // Root opens expanded — it's the household triage landing.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('true');
  });

  it('flags a zero-on-hand supply "Out" but not a merely-low one', async () => {
    const fixture = await create({ kind: 'root', overview: BUSY });

    // Glue is at 0 on hand → "Out"; Tape is at 2 → just low, no badge.
    expect(row(fixture, 'Glue')?.textContent).toContain('Out');
    expect(row(fixture, 'Tape')?.textContent).not.toContain('Out');

    // The badge carries an accessible name (matching the module's destructive-badge convention).
    const badge = row(fixture, 'Glue')?.querySelector('[aria-label="Out of stock"]');
    expect(badge?.textContent?.trim()).toBe('Out');
    expect(row(fixture, 'Tape')?.querySelector('[aria-label="Out of stock"]')).toBeNull();
  });

  it('renders nothing at the root when nothing is running low', async () => {
    const fixture = await create({ kind: 'root', overview: ALL_QUIET });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });

  it('stays hidden at the root when only lent/unplaced are nonzero (they are not alerts)', async () => {
    const fixture = await create({
      kind: 'root',
      overview: { ...ALL_QUIET, lentCount: 3, unplacedCount: 4 },
    });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });

  // ----- Place mode -----

  it('renders the area-scoped low-stock panel, collapsed by default, no lent/unplaced panel', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 3 });

    const content = text(fixture);
    expect(content).toContain('Low supplies stored here');
    // Lent is now a status chip, not an alert panel; unplaced never applies to a place.
    expect(content).not.toContain('Items lent from here');
    expect(content).not.toContain('Unplaced');

    // The View-all link carries the area filter so it composes with the items-list deep-link reader.
    expect(links(fixture)).toEqual(['/inventory/items?area=garage&lowStock=true']);

    // Drilled re-seeds collapsed so the panel doesn't fight the place browser for vertical space.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('false');
  });

  it('carries the container filter when drilled into a container', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { container: 'chest' }, lowStockCount: 1 });

    expect(links(fixture)).toEqual(['/inventory/items?container=chest&lowStock=true']);
  });

  it('renders nothing in a drilled place with no low stock', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 0 });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });
});
