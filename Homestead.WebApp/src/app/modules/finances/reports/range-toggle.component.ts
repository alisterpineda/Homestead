import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { HlmButton } from '@/shared/components/spartan/button';
import type { NetWorthRange } from '@/api/generated/models/net-worth-range';

interface Option {
  readonly value: NetWorthRange;
  readonly label: string;
}

const OPTIONS: readonly Option[] = [
  { value: 'OneMonth', label: '1M' },
  { value: 'ThreeMonths', label: '3M' },
  { value: 'SixMonths', label: '6M' },
  { value: 'OneYear', label: '1Y' },
  { value: 'All', label: 'All' },
];

@Component({
  selector: 'app-net-worth-range-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton],
  template: `
    <div role="radiogroup" aria-label="Date range" class="inline-flex rounded-md border border-border bg-card p-0.5">
      @for (opt of options; track opt.value) {
        <button
          type="button"
          hlmBtn
          [variant]="value() === opt.value ? 'default' : 'ghost'"
          size="sm"
          role="radio"
          [attr.aria-checked]="value() === opt.value"
          class="px-3 py-1 text-xs"
          (click)="select(opt.value)"
        >
          {{ opt.label }}
        </button>
      }
    </div>
  `,
})
export class RangeToggleComponent {
  readonly value = input.required<NetWorthRange>();
  readonly valueChange = output<NetWorthRange>();

  protected readonly options = OPTIONS;

  protected select(next: NetWorthRange): void {
    if (this.value() === next) return;
    this.valueChange.emit(next);
  }
}
