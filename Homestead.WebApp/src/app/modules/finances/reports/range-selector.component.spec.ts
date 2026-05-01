import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RangeSelectorComponent } from './range-selector.component';
import { resolveWindow } from './resolve-window';
import { localIsoDate } from '../shared/date';
import type { NetWorthRangePref } from '@/shared/services/user-preferences.service';

@Component({
  imports: [RangeSelectorComponent],
  template: `
    <app-net-worth-range-selector
      [value]="value()"
      [responseFrom]="responseFrom()"
      [responseTo]="responseTo()"
      (valueChange)="onChange($event)"
    />
  `,
})
class HostComponent {
  readonly value = signal<NetWorthRangePref>({ kind: 'All' });
  readonly responseFrom = signal<string | null>(null);
  readonly responseTo = signal<string | null>(null);
  lastEmitted: NetWorthRangePref | null = null;

  onChange(next: NetWorthRangePref): void {
    this.lastEmitted = next;
    this.value.set(next);
  }
}

// Local-constructed fixed date so date math is stable regardless of CI runner TZ.
const FIXED_DATE = new Date(2026, 3, 30, 12, 0, 0);

const iso = (d: Date | null | undefined): string | null => (d ? localIsoDate(d) : null);

describe('resolveWindow', () => {
  it('ThisMonth runs from first-of-month to today', () => {
    const w = resolveWindow({ kind: 'ThisMonth' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2026-04-01');
    expect(iso(w.to)).toBe('2026-04-30');
  });

  it('ThreeMonths spans the last three calendar months including current', () => {
    const w = resolveWindow({ kind: 'ThreeMonths' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2026-02-01');
    expect(iso(w.to)).toBe('2026-04-30');
  });

  it('SixMonths spans the last six calendar months including current', () => {
    const w = resolveWindow({ kind: 'SixMonths' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2025-11-01');
    expect(iso(w.to)).toBe('2026-04-30');
  });

  it('TwelveMonths spans the last twelve calendar months including current', () => {
    const w = resolveWindow({ kind: 'TwelveMonths' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2025-05-01');
    expect(iso(w.to)).toBe('2026-04-30');
  });

  it('YearToDate starts at Jan 1', () => {
    const w = resolveWindow({ kind: 'YearToDate' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2026-01-01');
  });

  it('LastYear spans Jan 1 to Dec 31 of prior year', () => {
    const w = resolveWindow({ kind: 'LastYear' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2025-01-01');
    expect(iso(w.to)).toBe('2025-12-31');
  });

  it('All returns nulls (caller falls back to server window)', () => {
    const w = resolveWindow({ kind: 'All' }, FIXED_DATE)!;
    expect(w.from).toBeNull();
    expect(w.to).toBeNull();
  });

  it('Custom passes through ISO bounds', () => {
    const w = resolveWindow({ kind: 'Custom', from: '2025-05-01', to: '2025-08-31' }, FIXED_DATE)!;
    expect(iso(w.from)).toBe('2025-05-01');
    expect(iso(w.to)).toBe('2025-08-31');
  });
});

describe('RangeSelectorComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(FIXED_DATE);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the trigger label from the resolved window', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.value.set({ kind: 'TwelveMonths' });
    fixture.detectChanges();
    const trigger = fixture.debugElement.query(By.css('button[hlmPopoverTrigger]'));
    const text = (trigger.nativeElement.textContent ?? '').trim();
    expect(text).toContain('2025');
    expect(text).toContain('2026');
  });

  it('renders "All Dates" when value is All and no response window', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const trigger = fixture.debugElement.query(By.css('button[hlmPopoverTrigger]'));
    expect((trigger.nativeElement.textContent ?? '').trim()).toContain('All Dates');
  });

  it('renders the response window when value is All', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.responseFrom.set('2024-06-01T00:00:00Z');
    fixture.componentInstance.responseTo.set('2026-04-30T23:59:59Z');
    fixture.detectChanges();
    const trigger = fixture.debugElement.query(By.css('button[hlmPopoverTrigger]'));
    const text = (trigger.nativeElement.textContent ?? '').trim();
    expect(text).toContain('2024');
    expect(text).toContain('2026');
  });

  it('disables the chevrons when range is All', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const chevronButtons = fixture.debugElement
      .queryAll(By.css('button'))
      .filter(b => b.nativeElement.getAttribute('aria-label')?.startsWith('Previous')
        || b.nativeElement.getAttribute('aria-label')?.startsWith('Next'));
    expect(chevronButtons.length).toBe(2);
    for (const b of chevronButtons) {
      expect(b.nativeElement.disabled).toBe(true);
    }
  });

  it('chevron forward is disabled for ThreeMonths because next window would be in the future', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.value.set({ kind: 'ThreeMonths' });
    fixture.detectChanges();
    const next = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => b.nativeElement.getAttribute('aria-label') === 'Next range')!;
    expect(next.nativeElement.disabled).toBe(true);
  });

  it('chevron forward shifts a past Custom window to the next calendar-aligned window', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.value.set({ kind: 'Custom', from: '2025-01-01', to: '2025-03-31' });
    fixture.detectChanges();
    const next = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => b.nativeElement.getAttribute('aria-label') === 'Next range')!;
    expect(next.nativeElement.disabled).toBe(false);
    next.nativeElement.click();
    fixture.detectChanges();
    const emitted = fixture.componentInstance.lastEmitted!;
    expect(emitted.kind).toBe('Custom');
    expect(emitted.from).toBe('2025-04-01');
    expect(emitted.to).toBe('2025-06-30');
  });

  it('chevron forward is disabled when the next stepped window would cross the current month', () => {
    const fixture = TestBed.createComponent(HostComponent);
    // Window ends Dec 2025; +12 → Dec 2026 which is past today (Apr 2026).
    fixture.componentInstance.value.set({ kind: 'LastYear' });
    fixture.detectChanges();
    const next = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => b.nativeElement.getAttribute('aria-label') === 'Next range')!;
    expect(next.nativeElement.disabled).toBe(true);
  });

  it('chevron backward shifts ThreeMonths to the previous calendar-aligned 3-month window', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.value.set({ kind: 'ThreeMonths' });
    fixture.detectChanges();
    const prev = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => b.nativeElement.getAttribute('aria-label') === 'Previous range')!;
    prev.nativeElement.click();
    fixture.detectChanges();
    const emitted = fixture.componentInstance.lastEmitted!;
    expect(emitted.kind).toBe('Custom');
    // Original window 2026-02-01 to 2026-04-30 → -3 months → 2025-11-01 to 2026-01-31.
    expect(emitted.from).toBe('2025-11-01');
    expect(emitted.to).toBe('2026-01-31');
  });

  it('clicking a preset option emits valueChange with that kind', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const trigger = fixture.debugElement.query(By.css('button[hlmPopoverTrigger]'));
    trigger.nativeElement.click();
    fixture.detectChanges();

    // Find the "This Month" preset button by visible text inside the popover.
    const presetButtons = Array.from(document.querySelectorAll('button')) as HTMLButtonElement[];
    const target = presetButtons.find(b => (b.textContent ?? '').trim().startsWith('This Month'));
    expect(target).toBeDefined();
    target!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.lastEmitted).toEqual({ kind: 'ThisMonth' });
  });
});
