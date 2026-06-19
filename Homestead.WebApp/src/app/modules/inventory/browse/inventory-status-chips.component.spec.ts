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

  it('renders the Lent chip with count and deep-link at root', async () => {
    const fixture = await create({ kind: 'root', lentCount: 3 });

    const content = text(fixture);
    expect(content).toContain('Browse by status');
    expect(content).toContain('Lent out');
    expect(content).toContain('3');

    expect(links(fixture)).toEqual(['/inventory/items?lent=true']);
  });

  it('renders nothing at root when the lent count is zero', async () => {
    const fixture = await create({ kind: 'root', lentCount: 0 });

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

  it('renders a count-only Lent chip (no link) when drilled into a container', async () => {
    // A container drill carries null params — the list can't scope to a container, so the chip is
    // count-only (the matching items already render in the contents table below).
    const fixture = await create({ kind: 'place', lentCount: 1, lentParams: null });

    const content = text(fixture);
    expect(content).toContain('Browse by status');
    expect(content).toContain('Lent out');
    expect(content).toContain('1');
    expect(links(fixture)).toEqual([]);
  });

  it('renders nothing in a drilled place with nothing lent', async () => {
    const fixture = await create({ kind: 'place', lentCount: 0, lentParams: { lent: 'true', area: 'garage' } });

    expect(text(fixture).trim()).toBe('');
    expect(links(fixture)).toEqual([]);
  });
});
