import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BrnTooltip } from '@spartan-ng/brain/tooltip';
import { HlmDatePicker } from '@/shared/components/spartan/date-picker';
import { AiDatePickerInputComponent } from './ai-date-picker-input.component';

// The input is a date-picker trigger: it injects its parent picker, so it is hosted the way the detail page
// uses it.
@Component({
  imports: [HlmDatePicker, AiDatePickerInputComponent],
  template: `
    <hlm-date-picker [date]="date()">
      <app-ai-date-picker-input
        inputId="test-date"
        [aiPending]="pending()"
        approveLabel="Approve date"
        declineLabel="Decline date"
        [originalLabel]="originalLabel()"
        (approve)="approved = approved + 1"
        (decline)="declined = declined + 1"
      />
    </hlm-date-picker>
  `,
})
class HostComponent {
  date = signal<Date | undefined>(new Date(2025, 2, 15));
  pending = signal(false);
  originalLabel = signal<string | null>(null);
  approved = 0;
  declined = 0;
}

describe('AiDatePickerInputComponent', () => {
  function setup(pending: boolean) {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.pending.set(pending);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const button = (label: string) => el.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
    return { fixture, el, button };
  }

  it('shows approve/decline in place of the clear button while a suggestion is pending', () => {
    const { fixture, button } = setup(true);

    expect(button('Approve date')).not.toBeNull();
    expect(button('Decline date')).not.toBeNull();
    expect(button('Clear date')).toBeNull();
    expect(button('Open calendar')).not.toBeNull();

    button('Approve date')!.click();
    button('Decline date')!.click();
    button('Decline date')!.click();
    expect(fixture.componentInstance.approved).toBe(1);
    expect(fixture.componentInstance.declined).toBe(2);
  });

  // On the <input>, not the host: the host can't take focus, so a keyboard user would never see it.
  it('puts the original-value tooltip on the input, enabled only with a label', () => {
    const { fixture } = setup(true);
    const tooltip = fixture.debugElement.query(By.css('input')).injector.get(BrnTooltip);
    expect(tooltip.tooltipDisabled()).toBe(true);

    fixture.componentInstance.originalLabel.set('Original: Jan 1, 2025');
    fixture.detectChanges();

    expect(tooltip.brnTooltip()).toBe('Original: Jan 1, 2025');
    expect(tooltip.tooltipDisabled()).toBe(false);
  });

  it('renders the stock clear and calendar buttons without a pending suggestion', () => {
    const { el, button } = setup(false);

    expect(button('Approve date')).toBeNull();
    expect(button('Decline date')).toBeNull();
    expect(button('Clear date')).not.toBeNull();
    expect(button('Open calendar')).not.toBeNull();
    expect(el.querySelector<HTMLInputElement>('#test-date')).not.toBeNull();
  });
});
