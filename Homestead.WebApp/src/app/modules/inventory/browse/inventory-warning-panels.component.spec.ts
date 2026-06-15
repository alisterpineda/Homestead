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

  // ----- Root mode -----

  it('renders the three global tiles with previews and deep-links, open by default', async () => {
    const fixture = await create({ kind: 'root', overview: BUSY });

    const content = text(fixture);
    expect(content).toContain('Low stock');
    expect(content).toContain('Lent out');
    expect(content).toContain('Unplaced');
    expect(content).toContain('Glue');
    expect(content).toContain('0 tubes / 5');
    expect(content).toContain('Ladder');
    expect(content).toContain('Dave next door');
    expect(content).toContain('Adrift wrench');
    // 7 low-stock total with 2 previewed → "+5 more".
    expect(content).toContain('+5 more');

    expect(links(fixture)).toContain('/inventory/items?lowStock=true');
    expect(links(fixture)).toContain('/inventory/items?lent=true');
    expect(links(fixture)).toContain('/inventory/items?area=unplaced&kind=items');

    // Root opens expanded — it's the household triage landing.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('true');
  });

  it('renders nothing at the root when everything is quiet', async () => {
    const fixture = await create({ kind: 'root', overview: ALL_QUIET });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });

  // ----- Place mode -----

  it('renders area-scoped count-only panels, collapsed by default, no unplaced panel', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 3, lentCount: 2 });

    const content = text(fixture);
    expect(content).toContain('Low supplies stored here');
    expect(content).toContain('Items lent from here');
    // Unplaced is the absence of a place — never a drilled panel.
    expect(content).not.toContain('Unplaced');

    // View-all links carry the area filter so they compose with the items-list deep-link reader.
    expect(links(fixture)).toContain('/inventory/items?area=garage&lowStock=true');
    expect(links(fixture)).toContain('/inventory/items?area=garage&lent=true');

    // Drilled re-seeds collapsed so the panels don't fight the place browser for vertical space.
    expect(trigger(fixture)?.getAttribute('aria-expanded')).toBe('false');
  });

  it('omits the lent panel when only low-stock is present', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 1, lentCount: 0 });

    const content = text(fixture);
    expect(content).toContain('Low supplies stored here');
    expect(content).not.toContain('Items lent from here');
    expect(links(fixture)).toEqual(['/inventory/items?area=garage&lowStock=true']);
  });

  it('carries the container filter when drilled into a container', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { container: 'chest' }, lowStockCount: 1, lentCount: 1 });

    // The same panels, but View-all links compose with the items-list container filter.
    expect(links(fixture)).toContain('/inventory/items?container=chest&lowStock=true');
    expect(links(fixture)).toContain('/inventory/items?container=chest&lent=true');
  });

  it('renders nothing in a drilled place with no warnings', async () => {
    const fixture = await create({ kind: 'place', viewAllParams: { area: 'garage' }, lowStockCount: 0, lentCount: 0 });

    expect(trigger(fixture)).toBeNull();
    expect(text(fixture).trim()).toBe('');
  });
});
