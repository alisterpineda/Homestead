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

  function create(
    status: ItemStatus, showDefault = false, note: string | null = null,
  ): ComponentFixture<ItemStatusBadgeComponent> {
    const fixture = TestBed.createComponent(ItemStatusBadgeComponent);
    fixture.componentRef.setInput('status', status);
    fixture.componentRef.setInput('showDefault', showDefault);
    fixture.componentRef.setInput('note', note);
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
    const fixture = create('Archived');

    const span = host(fixture).querySelector('span');
    expect(span).not.toBeNull();
    expect(span!.textContent?.trim()).toBe('Archived');
    expect(host(fixture).classList.contains('hidden')).toBe(false);
  });

  it('renders a Lent note as "to <borrower>" beside the pill', () => {
    const fixture = create('Lent', false, 'Dave');

    const spans = host(fixture).querySelectorAll('span');
    expect(spans.length).toBe(2);
    expect(spans[0].textContent?.trim()).toBe('Lent');
    expect(spans[1].textContent?.trim()).toBe('to Dave');
  });

  it('renders an Archived note verbatim beside the pill', () => {
    const fixture = create('Archived', false, 'Sold to Sam');

    const spans = host(fixture).querySelectorAll('span');
    expect(spans.length).toBe(2);
    expect(spans[1].textContent?.trim()).toBe('Sold to Sam');
  });

  it('renders no note for Active, even when one is somehow supplied', () => {
    const fixture = create('Active', true, 'ignored');

    const spans = host(fixture).querySelectorAll('span');
    expect(spans.length).toBe(1);
    expect(spans[0].textContent?.trim()).toBe('Active');
  });

  it('renders no note text when the note is blank', () => {
    const fixture = create('Lent', false, '   ');

    const spans = host(fixture).querySelectorAll('span');
    expect(spans.length).toBe(1);
    expect(spans[0].textContent?.trim()).toBe('Lent');
  });
});
