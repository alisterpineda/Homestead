import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { ItemStatusBadgeComponent } from './item-status-badge.component';
import type { ItemStatus } from '@/api/generated/models/item-status';

describe('ItemStatusBadgeComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()],
    });
  });

  function create(status: ItemStatus, showDefault = false): ComponentFixture<ItemStatusBadgeComponent> {
    const fixture = TestBed.createComponent(ItemStatusBadgeComponent);
    fixture.componentRef.setInput('status', status);
    fixture.componentRef.setInput('showDefault', showDefault);
    fixture.detectChanges();
    return fixture;
  }

  function host(fixture: ComponentFixture<ItemStatusBadgeComponent>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('renders no badge for the default Active status and collapses the host', () => {
    const fixture = create('Active');

    // No chip is emitted at all — Active is the resting default state, suppressed by default.
    expect(host(fixture).querySelector('span')).toBeNull();
    expect(host(fixture).textContent?.trim()).toBe('');
    // The host collapses to display:none so it stops occupying a flex `gap` slot (no phantom gap).
    expect(host(fixture).classList.contains('hidden')).toBe(true);
  });

  it('renders a quiet secondary Active chip when a host opts in via showDefault', () => {
    const fixture = create('Active', true);

    // Dense list/table opts in: Active shows its chip, styled as the quiet Spartan `secondary` tone.
    const span = host(fixture).querySelector('span');
    expect(span).not.toBeNull();
    expect(span!.textContent?.trim()).toBe('Active');
    expect(span!.className).toContain('bg-secondary');
    expect(host(fixture).classList.contains('hidden')).toBe(false);
  });

  it('renders the chip with its label for the non-default Lent status', () => {
    const fixture = create('Lent');

    const span = host(fixture).querySelector('span');
    expect(span).not.toBeNull();
    expect(span!.textContent?.trim()).toBe('Lent');
    expect(host(fixture).classList.contains('hidden')).toBe(false);
  });

  it('renders the chip for a terminal non-default status', () => {
    const fixture = create('Sold');

    const span = host(fixture).querySelector('span');
    expect(span).not.toBeNull();
    expect(span!.textContent?.trim()).toBe('Sold');
    expect(host(fixture).classList.contains('hidden')).toBe(false);
  });
});
