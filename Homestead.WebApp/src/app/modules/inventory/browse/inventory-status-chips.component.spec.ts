import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { InventoryStatusChipsComponent, type StatusChipsScope } from './inventory-status-chips.component';

describe('InventoryStatusChipsComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
      ],
    });
  });

  async function create(scope: StatusChipsScope): Promise<ComponentFixture<InventoryStatusChipsComponent>> {
    const fixture = TestBed.createComponent(InventoryStatusChipsComponent);
    fixture.componentRef.setInput('scope', scope);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<InventoryStatusChipsComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function links(fixture: ComponentFixture<InventoryStatusChipsComponent>): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  // ----- Root mode -----

  it('renders Unplaced + Lent chips with counts and deep-links at root', async () => {
    const fixture = await create({ kind: 'root', unplacedCount: 2, lentCount: 3 });

    const content = text(fixture);
    expect(content).toContain('Browse by status');
    expect(content).toContain('Unplaced');
    expect(content).toContain('Lent out');
    expect(content).toContain('2');
    expect(content).toContain('3');

    expect(links(fixture)).toEqual([
      '/inventory/items?area=unplaced&kind=items',
      '/inventory/items?lent=true',
    ]);
  });

  it('hides zero-count chips at root', async () => {
    const fixture = await create({ kind: 'root', unplacedCount: 0, lentCount: 4 });

    const content = text(fixture);
    expect(content).not.toContain('Unplaced');
    expect(content).toContain('Lent out');
    expect(links(fixture)).toEqual(['/inventory/items?lent=true']);
  });

  it('renders nothing at root when both counts are zero', async () => {
    const fixture = await create({ kind: 'root', unplacedCount: 0, lentCount: 0 });

    expect(text(fixture).trim()).toBe('');
    expect(links(fixture)).toEqual([]);
  });

  // ----- Place mode -----

  it('renders only the Lent chip with the place filter when drilled', async () => {
    const fixture = await create({ kind: 'place', lentCount: 2, lentParams: { lent: 'true', area: 'garage' } });

    const content = text(fixture);
    expect(content).toContain('Browse by status');
    expect(content).toContain('Lent out');
    // Unplaced is the absence of a place — never a place chip.
    expect(content).not.toContain('Unplaced');

    expect(links(fixture)).toEqual(['/inventory/items?lent=true&area=garage']);
  });

  it('carries the container filter when drilled into a container', async () => {
    const fixture = await create({ kind: 'place', lentCount: 1, lentParams: { lent: 'true', container: 'chest' } });

    expect(links(fixture)).toEqual(['/inventory/items?lent=true&container=chest']);
  });

  it('renders nothing in a drilled place with nothing lent', async () => {
    const fixture = await create({ kind: 'place', lentCount: 0, lentParams: { lent: 'true', area: 'garage' } });

    expect(text(fixture).trim()).toBe('');
    expect(links(fixture)).toEqual([]);
  });
});
