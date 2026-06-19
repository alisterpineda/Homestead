import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert, lucideChevronRight } from '@ng-icons/lucide';
import { InventoryWarningSectionComponent } from './inventory-warning-section.component';

/**
 * Host harness for the count-only mode (no projected rows) — drives the section's inputs directly.
 */
@Component({
  imports: [InventoryWarningSectionComponent],
  template: `
    <app-inventory-warning-section
      [icon]="icon"
      [label]="label"
      [count]="count"
      [previewCount]="previewCount"
      [viewAllParams]="viewAllParams"
    />
  `,
})
class CountOnlyHost {
  icon = 'lucideTriangleAlert';
  label = 'Low consumables stored here';
  count = 3;
  previewCount = 0;
  viewAllParams: Record<string, string> = { area: 'garage', lowStock: 'true' };
}

/**
 * Host harness for the preview mode — projects `<li>` rows into the section's `<ng-content />`.
 */
@Component({
  imports: [InventoryWarningSectionComponent],
  template: `
    <app-inventory-warning-section
      icon="lucideTriangleAlert"
      label="Low stock"
      [count]="count"
      [previewCount]="previewCount"
      [viewAllParams]="viewAllParams"
    >
      <li>Glue</li>
      <li>Tape</li>
    </app-inventory-warning-section>
  `,
})
class PreviewHost {
  count = 7;
  previewCount = 2;
  viewAllParams: Record<string, string> = { lowStock: 'true' };
}

describe('InventoryWarningSectionComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideIcons({ lucideTriangleAlert, lucideChevronRight }),
      ],
    });
  });

  async function createPreview(): Promise<ComponentFixture<PreviewHost>> {
    const fixture = TestBed.createComponent(PreviewHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  async function createCountOnly(): Promise<ComponentFixture<CountOnlyHost>> {
    const fixture = TestBed.createComponent(CountOnlyHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<unknown>): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function links(fixture: ComponentFixture<unknown>): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[href]'),
    ).map(a => decodeURIComponent(a.getAttribute('href') ?? ''));
  }

  it('renders the header label and count', async () => {
    const fixture = await createPreview();

    const content = text(fixture);
    expect(content).toContain('Low stock');
    expect(content).toContain('7');
    expect(content).toContain('View all');
  });

  it('renders the projected preview rows', async () => {
    const fixture = await createPreview();

    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li'));
    expect(rows.map(li => li.textContent?.trim())).toEqual(['Glue', 'Tape']);
  });

  it('shows "+N more" when the count exceeds the preview length', async () => {
    const fixture = await createPreview();

    // 7 low-stock total with 2 previewed → "+5 more".
    expect(text(fixture)).toContain('+5 more');
  });

  it('omits the overflow line when the count matches the preview length', async () => {
    const fixture = TestBed.createComponent(PreviewHost);
    fixture.componentInstance.count = 2;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    // The rows still render — so the absence of "more" is the boundary working, not an empty section.
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('li'));
    expect(rows.map(li => li.textContent?.trim())).toEqual(['Glue', 'Tape']);
    expect(text(fixture)).not.toContain('more');
  });

  it('exposes exactly one deep-link carrying the given viewAllParams', async () => {
    const fixture = await createPreview();

    expect(links(fixture)).toEqual(['/inventory/items?lowStock=true']);
  });

  it('renders count-only (no projected rows) as just the header — no list or overflow line', async () => {
    const fixture = await createCountOnly();

    const content = text(fixture);
    expect(content).toContain('Low consumables stored here');
    expect(content).toContain('3');
    expect(content).not.toContain('more');

    // previewCount defaults to 0 → no <ul> rendered at all.
    expect((fixture.nativeElement as HTMLElement).querySelector('ul')).toBeNull();

    // The single deep-link carries the place filter.
    expect(links(fixture)).toEqual(['/inventory/items?area=garage&lowStock=true']);
  });
});
