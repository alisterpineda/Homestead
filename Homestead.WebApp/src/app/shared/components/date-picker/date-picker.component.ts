import { Component, ChangeDetectionStrategy, computed, signal, input, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmCalendarImports } from '@/shared/components/spartan/calendar';
import { HlmButton } from '@/shared/components/spartan/button';

@Component({
  selector: 'app-date-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmCalendarImports,
    HlmButton,
  ],
  providers: [provideIcons({ lucideChevronDown })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [attr.aria-label]="ariaLabel()"
        class="border-input bg-background hover:bg-accent dark:bg-input/30 dark:hover:bg-input/50 hover:text-accent-foreground focus-visible:ring-ring inline-flex h-9 w-full cursor-default items-center justify-between gap-2 rounded-md border px-3 py-2 text-left text-sm font-normal whitespace-nowrap transition-all focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50"
        [class.text-muted-foreground]="!date()"
      >
        <span class="truncate">{{ displayDate() ?? placeholder() }}</span>
        <ng-icon hlm size="sm" name="lucideChevronDown" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-auto p-0" align="start">
        <hlm-calendar calendarClass="border-0" [date]="date() ?? undefined" (dateChange)="onDateSelected($event)" />
        <div class="border-t p-1">
          <button hlmBtn variant="ghost" size="sm" class="w-full" [disabled]="!date()" (click)="clearDate()">
            Clear
          </button>
        </div>
      </div>
    </div>
  `,
})
export class DatePickerComponent {
  date = model<Date | null>(null);
  placeholder = input('Select date');
  ariaLabel = input<string | undefined>(undefined);

  popoverState = signal<'open' | 'closed'>('closed');

  displayDate = computed(() => {
    const d = this.date();
    return d
      ? d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
      : null;
  });

  onDateSelected(date: Date): void {
    this.date.set(date);
    this.popoverState.set('closed');
  }

  clearDate(): void {
    this.date.set(null);
    this.popoverState.set('closed');
  }
}
