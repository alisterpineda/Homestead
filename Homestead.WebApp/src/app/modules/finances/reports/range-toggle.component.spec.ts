import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { RangeToggleComponent } from './range-toggle.component';
import type { NetWorthRange } from '@/api/generated/models/net-worth-range';

@Component({
  imports: [RangeToggleComponent],
  template: `<app-net-worth-range-toggle [value]="value()" (valueChange)="onChange($event)" />`,
})
class HostComponent {
  readonly value = signal<NetWorthRange>('All');
  lastEmitted: NetWorthRange | null = null;
  onChange(next: NetWorthRange): void {
    this.lastEmitted = next;
  }
}

describe('RangeToggleComponent', () => {
  it('renders all five options and marks the active one', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const buttons = fixture.debugElement.queryAll(By.css('button'));
    expect(buttons.length).toBe(5);
    const labels = buttons.map(b => (b.nativeElement.textContent ?? '').trim());
    expect(labels).toEqual(['1M', '3M', '6M', '1Y', 'All']);
    // 'All' is the default → its aria-checked should be true.
    const allBtn = buttons.find(b => (b.nativeElement.textContent ?? '').trim() === 'All')!;
    expect(allBtn.nativeElement.getAttribute('aria-checked')).toBe('true');
  });

  it('emits valueChange when a different option is clicked', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const oneMonth = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => (b.nativeElement.textContent ?? '').trim() === '1M')!;
    oneMonth.nativeElement.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.lastEmitted).toBe('OneMonth');
  });

  it('does not re-emit when the active option is clicked again', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const all = fixture.debugElement
      .queryAll(By.css('button'))
      .find(b => (b.nativeElement.textContent ?? '').trim() === 'All')!;
    all.nativeElement.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.lastEmitted).toBeNull();
  });
});
