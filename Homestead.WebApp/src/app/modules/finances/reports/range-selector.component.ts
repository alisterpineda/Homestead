import { Component, ChangeDetectionStrategy, computed, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideCheck, lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmButton } from '@/shared/components/spartan/button';
import type { NetWorthRange } from '@/api/generated/models/net-worth-range';
import type { NetWorthRangePref } from '@/shared/services/user-preferences.service';
import { MonthPickerComponent, type MonthValue } from './month-picker.component';
import { resolveWindow } from './resolve-window';

interface PresetOption {
  readonly kind: NetWorthRange;
  readonly label: string;
}

const PRESETS: readonly PresetOption[] = [
  { kind: 'ThisMonth', label: 'This Month' },
  { kind: 'ThreeMonths', label: 'Last 3 Months' },
  { kind: 'SixMonths', label: 'Last 6 Months' },
  { kind: 'TwelveMonths', label: 'Last 12 Months' },
  { kind: 'YearToDate', label: 'Year To Date' },
  { kind: 'LastYear', label: 'Last Year' },
  { kind: 'All', label: 'All Dates' },
  { kind: 'Custom', label: 'Custom' },
];

const RANGE_LABEL_FORMATTER = new Intl.DateTimeFormat(undefined, { month: 'short', year: 'numeric' });

function toIsoDate(d: Date): string {
  const year = d.getUTCFullYear();
  const month = String(d.getUTCMonth() + 1).padStart(2, '0');
  const day = String(d.getUTCDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function parseIsoDate(iso: string): Date | null {
  const parsed = new Date(`${iso}T00:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function dateToMonthValue(d: Date): MonthValue {
  return { year: d.getUTCFullYear(), month: d.getUTCMonth() + 1 };
}

function monthValueToFromDate(v: MonthValue): Date {
  return new Date(Date.UTC(v.year, v.month - 1, 1));
}

function monthValueToToDate(v: MonthValue): Date {
  // Last day of the month
  return new Date(Date.UTC(v.year, v.month, 0));
}

function shiftMonthValue(v: MonthValue, deltaMonths: number): MonthValue {
  const total = v.year * 12 + (v.month - 1) + deltaMonths;
  return { year: Math.floor(total / 12), month: (((total % 12) + 12) % 12) + 1 };
}

function monthsBetween(a: MonthValue, b: MonthValue): number {
  return (b.year - a.year) * 12 + (b.month - a.month);
}

@Component({
  selector: 'app-net-worth-range-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmButton, MonthPickerComponent],
  providers: [provideIcons({ lucideCalendar, lucideCheck, lucideChevronLeft, lucideChevronRight })],
  template: `
    <div class="inline-flex items-center rounded-full border border-border bg-card">
      <button
        hlmBtn
        type="button"
        variant="ghost"
        size="icon"
        class="h-9 w-9 rounded-full"
        aria-label="Previous range"
        [disabled]="cannotStep()"
        (click)="step(-1)"
      >
        <ng-icon hlm size="sm" name="lucideChevronLeft" />
      </button>
      <div hlmPopover [state]="popoverState()" (stateChanged)="onPopoverStateChanged($event)">
        <button
          hlmPopoverTrigger
          type="button"
          class="hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring inline-flex h-9 items-center gap-2 rounded-full px-3 text-sm font-medium transition-colors focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none"
        >
          <ng-icon hlm size="sm" name="lucideCalendar" />
          <span class="tabular-nums">{{ rangeLabel() }}</span>
        </button>
        <div hlmPopoverContent *hlmPopoverPortal class="w-72 p-2" align="center">
          <ul class="flex flex-col">
            @for (preset of presets; track preset.kind) {
              <li>
                <button
                  hlmBtn
                  type="button"
                  variant="ghost"
                  size="sm"
                  class="w-full justify-between"
                  (click)="selectPreset(preset.kind)"
                >
                  <span>{{ preset.label }}</span>
                  @if (preset.kind === value().kind) {
                    <ng-icon hlm size="sm" name="lucideCheck" />
                  }
                </button>
              </li>
            }
          </ul>
          @if (isCustomMode()) {
            <div class="mt-3 border-t pt-3">
              <div class="grid gap-2">
                <label class="text-xs font-medium text-muted-foreground">
                  Start Date
                  <app-month-picker
                    class="mt-1 block"
                    [value]="customFrom()"
                    (valueChange)="setCustomFrom($event)"
                    [max]="customTo()"
                    ariaLabel="Custom range start month"
                  />
                </label>
                <label class="text-xs font-medium text-muted-foreground">
                  End Date
                  <app-month-picker
                    class="mt-1 block"
                    [value]="customTo()"
                    (valueChange)="setCustomTo($event)"
                    [min]="customFrom()"
                    ariaLabel="Custom range end month"
                  />
                </label>
              </div>
              <div class="mt-3 flex justify-end gap-2">
                <button hlmBtn type="button" variant="ghost" size="sm" (click)="cancelCustom()">
                  Cancel
                </button>
                <button hlmBtn type="button" size="sm" (click)="applyCustom()">
                  Apply
                </button>
              </div>
            </div>
          }
        </div>
      </div>
      <button
        hlmBtn
        type="button"
        variant="ghost"
        size="icon"
        class="h-9 w-9 rounded-full"
        aria-label="Next range"
        [disabled]="cannotStep()"
        (click)="step(1)"
      >
        <ng-icon hlm size="sm" name="lucideChevronRight" />
      </button>
    </div>
  `,
})
export class RangeSelectorComponent {
  readonly value = input.required<NetWorthRangePref>();
  readonly valueChange = output<NetWorthRangePref>();
  readonly responseFrom = input<string | null>(null);
  readonly responseTo = input<string | null>(null);

  protected readonly presets = PRESETS;
  protected readonly popoverState = signal<'open' | 'closed'>('closed');
  protected readonly isCustomMode = signal(false);
  protected readonly customFrom = signal<MonthValue>({ year: new Date().getUTCFullYear(), month: 1 });
  protected readonly customTo = signal<MonthValue>(dateToMonthValue(new Date()));

  protected readonly cannotStep = computed(() => this.value().kind === 'All');

  protected readonly rangeLabel = computed(() => {
    const pref = this.value();
    if (pref.kind === 'Custom' && pref.from && pref.to) {
      return this.formatRange(parseIsoDate(pref.from), parseIsoDate(pref.to));
    }
    if (pref.kind === 'All') {
      const responseFrom = this.responseFrom();
      const responseTo = this.responseTo();
      if (!responseFrom || !responseTo) return 'All Dates';
      return this.formatRange(new Date(responseFrom), new Date(responseTo));
    }
    const today = new Date();
    const window = resolveWindow(pref, today);
    if (!window) return '—';
    return this.formatRange(window.from, window.to);
  });

  protected onPopoverStateChanged(state: 'open' | 'closed'): void {
    this.popoverState.set(state);
    if (state === 'open') {
      this.seedCustomEditFromValue();
      this.isCustomMode.set(this.value().kind === 'Custom');
    } else {
      this.isCustomMode.set(false);
    }
  }

  protected selectPreset(kind: NetWorthRange): void {
    if (kind === 'Custom') {
      this.seedCustomEditFromValue();
      this.isCustomMode.set(true);
      return;
    }
    this.valueChange.emit({ kind });
    this.popoverState.set('closed');
  }

  protected setCustomFrom(next: MonthValue): void {
    this.customFrom.set(next);
  }

  protected setCustomTo(next: MonthValue): void {
    this.customTo.set(next);
  }

  protected cancelCustom(): void {
    this.popoverState.set('closed');
  }

  protected applyCustom(): void {
    const from = monthValueToFromDate(this.customFrom());
    const to = monthValueToToDate(this.customTo());
    if (from > to) return;
    this.valueChange.emit({ kind: 'Custom', from: toIsoDate(from), to: toIsoDate(to) });
    this.popoverState.set('closed');
  }

  protected step(direction: -1 | 1): void {
    const pref = this.value();
    if (pref.kind === 'All') return;
    const today = new Date();
    const window = pref.kind === 'Custom' && pref.from && pref.to
      ? { from: parseIsoDate(pref.from), to: parseIsoDate(pref.to) }
      : resolveWindow(pref, today);
    if (!window?.from || !window.to) return;
    // Snap to month boundaries before shifting so day-of-month overflow (e.g. Dec 31
    // − 6 months → Jul 1 in raw Date math) cannot drift the window.
    const fromMonth = dateToMonthValue(window.from);
    const toMonth = dateToMonthValue(window.to);
    const span = Math.max(1, monthsBetween(fromMonth, toMonth) + 1);
    const nextFromMonth = shiftMonthValue(fromMonth, direction * span);
    const nextToMonth = shiftMonthValue(toMonth, direction * span);
    this.valueChange.emit({
      kind: 'Custom',
      from: toIsoDate(monthValueToFromDate(nextFromMonth)),
      to: toIsoDate(monthValueToToDate(nextToMonth)),
    });
  }

  private seedCustomEditFromValue(): void {
    const pref = this.value();
    if (pref.kind === 'Custom' && pref.from && pref.to) {
      const from = parseIsoDate(pref.from);
      const to = parseIsoDate(pref.to);
      if (from) this.customFrom.set(dateToMonthValue(from));
      if (to) this.customTo.set(dateToMonthValue(to));
      return;
    }
    const today = new Date();
    const window = resolveWindow(pref, today);
    if (window?.from && window.to) {
      this.customFrom.set(dateToMonthValue(window.from));
      this.customTo.set(dateToMonthValue(window.to));
    }
  }

  private formatRange(from: Date | null, to: Date | null): string {
    if (!from || !to) return '—';
    return `${RANGE_LABEL_FORMATTER.format(from)} – ${RANGE_LABEL_FORMATTER.format(to)}`;
  }
}
