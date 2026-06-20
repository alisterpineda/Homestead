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

  it('renders the Lent chip with count and deep-link', async () => {
    const fixture = await create({ lentCount: 3 });

    const content = text(fixture);
    expect(content).toContain('Browse by status');
    expect(content).toContain('Lent out');
    expect(content).toContain('3');

    expect(links(fixture)).toEqual(['/inventory/items?lent=true']);
  });

  it('renders nothing when the lent count is zero', async () => {
    const fixture = await create({ lentCount: 0 });

    expect(text(fixture).trim()).toBe('');
    expect(links(fixture)).toEqual([]);
  });
});
