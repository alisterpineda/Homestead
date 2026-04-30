import { Component, ChangeDetectionStrategy, computed, input, model, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmButton } from '@/shared/components/spartan/button';

export interface MonthValue {
  readonly year: number;
  // 1-indexed (1 = January, 12 = December)
  readonly month: number;
}

const MONTH_LABELS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function compare(a: MonthValue, b: MonthValue): number {
  if (a.year !== b.year) return a.year - b.year;
  return a.month - b.month;
}

@Component({
  selector: 'app-month-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmButton],
  providers: [provideIcons({ lucideChevronDown, lucideChevronLeft, lucideChevronRight })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="onStateChanged($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [attr.aria-label]="ariaLabel()"
        class="border-input bg-background hover:bg-accent dark:bg-input/30 dark:hover:bg-input/50 hover:text-accent-foreground focus-visible:ring-ring inline-flex h-9 w-full cursor-default items-center justify-between gap-2 rounded-md border px-3 py-2 text-left text-sm font-normal whitespace-nowrap transition-all focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50"
      >
        <span class="truncate">{{ displayLabel() }}</span>
        <ng-icon hlm size="sm" name="lucideChevronDown" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-64 p-3" align="start">
        <div class="mb-2 flex items-center justify-between">
          <button
            hlmBtn
            type="button"
            variant="ghost"
            size="icon"
            class="h-7 w-7"
            aria-label="Previous year"
            (click)="prevYear()"
          >
            <ng-icon hlm size="sm" name="lucideChevronLeft" />
          </button>
          <div class="text-sm font-medium tabular-nums">{{ viewYear() }}</div>
          <button
            hlmBtn
            type="button"
            variant="ghost"
            size="icon"
            class="h-7 w-7"
            aria-label="Next year"
            (click)="nextYear()"
          >
            <ng-icon hlm size="sm" name="lucideChevronRight" />
          </button>
        </div>
        <div class="grid grid-cols-3 gap-1">
          @for (cell of monthCells(); track cell.month) {
            <button
              hlmBtn
              type="button"
              size="sm"
              [variant]="cell.isSelected ? 'default' : 'ghost'"
              [disabled]="cell.disabled"
              class="h-9 w-full"
              (click)="selectMonth(cell.month)"
            >
              {{ cell.label }}
            </button>
          }
        </div>
      </div>
    </div>
  `,
})
export class MonthPickerComponent {
  readonly value = model.required<MonthValue>();
  readonly min = input<MonthValue | null>(null);
  readonly max = input<MonthValue | null>(null);
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly popoverState = signal<'open' | 'closed'>('closed');
  protected readonly viewYear = signal<number>(new Date().getFullYear());

  protected readonly displayLabel = computed(() => {
    const v = this.value();
    return `${MONTH_LABELS[v.month - 1]} ${v.year}`;
  });

  protected readonly monthCells = computed(() => {
    const year = this.viewYear();
    const selected = this.value();
    const min = this.min();
    const max = this.max();
    return MONTH_LABELS.map((label, idx) => {
      const month = idx + 1;
      const candidate: MonthValue = { year, month };
      const tooEarly = min ? compare(candidate, min) < 0 : false;
      const tooLate = max ? compare(candidate, max) > 0 : false;
      return {
        month,
        label,
        isSelected: selected.year === year && selected.month === month,
        disabled: tooEarly || tooLate,
      };
    });
  });

  protected onStateChanged(next: 'open' | 'closed'): void {
    this.popoverState.set(next);
    if (next === 'open') {
      this.viewYear.set(this.value().year);
    }
  }

  protected prevYear(): void {
    this.viewYear.update(y => y - 1);
  }

  protected nextYear(): void {
    this.viewYear.update(y => y + 1);
  }

  protected selectMonth(month: number): void {
    this.value.set({ year: this.viewYear(), month });
    this.popoverState.set('closed');
  }
}
