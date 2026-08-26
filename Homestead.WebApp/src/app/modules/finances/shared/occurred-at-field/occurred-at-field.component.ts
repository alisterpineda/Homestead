import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClock, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { parseDate, toIsoDateString } from '@/shared/utils';
import { tomorrowIso } from '../date';

// Date (+ optional time) picker shared by the per-shape transaction editors.
// The time field is collapsed by default — a date-only entry maps to noon UTC (see
// `occurred-at.ts`). The host owns the `date`/`time` values (controlled inputs) and
// composes/decomposes the actual instant; this component only renders + emits edits.
@Component({
  selector: 'app-occurred-at-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmLabel, HlmInput, HlmDatePicker, HlmDatePickerInput],
  providers: [provideIcons({ lucideClock, lucideX })],
  template: `
    <div class="space-y-2">
      <label hlmLabel [for]="dateId()">Date</label>
      <div class="flex flex-wrap items-center gap-2">
        <hlm-date-picker
          class="w-44"
          captionLayout="dropdown"
          [date]="dateValue()"
          [maxDate]="maxDateValue()"
          (dateChange)="onDateChange($event)"
        >
          <hlm-date-picker-input [inputId]="dateId()" placeholder="Select date" />
        </hlm-date-picker>
        @if (showTime()) {
          <input
            hlmInput
            [id]="timeId()"
            class="w-32"
            type="time"
            aria-label="Time"
            [value]="time()"
            (input)="timeChange.emit(asValue($event))"
          />
          <button
            type="button"
            hlmBtn
            variant="ghost"
            size="icon"
            aria-label="Clear time"
            (click)="clearTime()"
          >
            <ng-icon hlm name="lucideX" size="sm" />
          </button>
        } @else {
          <button type="button" hlmBtn variant="ghost" (click)="addTime()">
            <ng-icon hlm name="lucideClock" size="sm" />
            Add time
          </button>
        }
      </div>
    </div>
  `,
})
export class OccurredAtFieldComponent {
  date = input.required<string>(); // 'yyyy-MM-dd'
  time = input<string | null>(null); // 'HH:mm' local, null = noon-UTC default
  maxDate = input(tomorrowIso()); // encodes the "+1 day past today (UTC)" cap
  // Disambiguate input ids when more than one field is on a page.
  dateId = input('occurred-at-date');
  timeId = input('occurred-at-time');

  dateChange = output<string>();
  timeChange = output<string | null>();

  protected readonly dateValue = computed(() => parseDate(this.date()) ?? undefined);
  protected readonly maxDateValue = computed(() => parseDate(this.maxDate()) ?? undefined);

  protected onDateChange(date: Date | undefined): void {
    this.dateChange.emit(toIsoDateString(date ?? null) ?? '');
  }

  // Expanded iff a time is set. The host reflects every edit back through `time`,
  // so this stays in sync without a separate mutable flag.
  protected showTime = computed(() => this.time() != null);

  protected addTime(): void {
    this.timeChange.emit('12:00');
  }

  protected clearTime(): void {
    this.timeChange.emit(null);
  }

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
