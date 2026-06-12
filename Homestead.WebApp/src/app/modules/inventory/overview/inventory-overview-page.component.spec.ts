import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { InventoryOverviewPageComponent } from './inventory-overview-page.component';
import { InventoryOverviewApiService } from '@/api/services/inventory-overview-api.service';
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
  areas: [
    { id: 'garage', name: 'Garage', itemCount: 12 },
    { id: 'office', name: 'Office', itemCount: 0 },
  ],
};

class StubOverviewApi {
  response: GetInventoryOverviewResponse = ALL_QUIET;
  getOverview = () => of(this.response);
}

describe('InventoryOverviewPageComponent', () => {
  let api: StubOverviewApi;

  beforeEach(() => {
    api = new StubOverviewApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: InventoryOverviewApiService, useValue: api },
      ],
    });
  });

  async function create(): Promise<ComponentFixture<InventoryOverviewPageComponent>> {
    const fixture = TestBed.createComponent(InventoryOverviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<InventoryOverviewPageComponent>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders the tile counts and preview rows', async () => {
    api.response = BUSY;
    const fixture = await create();

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
  });

  it('deep-links each tile to the pre-filtered items list', async () => {
    api.response = BUSY;
    const fixture = await create();

    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => a.getAttribute('href'));
    expect(links).toContain('/inventory/items?lowStock=true');
    expect(links).toContain('/inventory/items?lent=true');
    expect(links).toContain('/inventory/items?area=unplaced&kind=items');
  });

  it('shows per-tile all-good empty states with no view-all links', async () => {
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('Nothing running low');
    expect(content).toContain('Nothing lent out');
    expect(content).toContain('Everything has a place');
    // The only link on an all-quiet dashboard is the browse-by-place empty-state hint.
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => a.getAttribute('href'));
    expect(links).toEqual(['/inventory/areas']);
  });

  it('renders a browse-by-place card per area with name and count', async () => {
    api.response = BUSY;
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('Browse by place');
    expect(content).toContain('Garage');
    expect(content).toContain('12');
    expect(content).toContain('Office');

    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => a.getAttribute('href'));
    expect(links).toContain('/inventory/items?area=garage');
    expect(links).toContain('/inventory/items?area=office');
  });

  it('shows the Unplaced card only when there are unplaced items', async () => {
    api.response = BUSY;
    let fixture = await create();
    let links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => a.getAttribute('href'));
    // Tile view-all link + place card share the same target — both present.
    expect(links.filter(l => l === '/inventory/items?area=unplaced&kind=items').length).toBe(2);

    api.response = { ...BUSY, unplacedCount: 0, unplacedItems: [] };
    fixture = await create();
    links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => a.getAttribute('href'));
    expect(links).not.toContain('/inventory/items?area=unplaced&kind=items');
    // Area cards still render without the Unplaced card.
    expect(links).toContain('/inventory/items?area=garage');
  });

  it('shows an empty hint linking to the Areas page when there is nothing to browse', async () => {
    const fixture = await create();

    const content = text(fixture);
    expect(content).toContain('No areas yet');
    const hint = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a[href="/inventory/areas"]',
    );
    expect(hint).not.toBeNull();
  });

  it('shows the error state when the fetch fails', async () => {
    api.getOverview = () => throwError(() => new Error('boom'));
    const fixture = await create();

    expect(text(fixture)).toContain('Failed to load overview');
  });

  it('hands the search term off to the items list on submit', async () => {
    const fixture = await create();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.componentInstance.searchInput.set('  drill ');
    fixture.componentInstance.onSearchSubmit(new Event('submit'));

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items'], {
      queryParams: { search: 'drill' },
    });
  });

  it('navigates with a null search param when the box is empty', async () => {
    const fixture = await create();
    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.componentInstance.onSearchSubmit(new Event('submit'));

    expect(navigateSpy).toHaveBeenCalledWith(['/inventory/items'], {
      queryParams: { search: null },
    });
  });
});
