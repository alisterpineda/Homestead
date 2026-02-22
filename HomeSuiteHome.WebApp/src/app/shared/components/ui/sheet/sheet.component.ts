import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { cn } from '@/shared/utils';

@Component({
  selector: 'app-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown)': 'onKeydown($event)',
  },
  template: `
    @if (open()) {
      <div class="fixed inset-0 z-50 flex justify-end">
        <div class="fixed inset-0 bg-black/30" (click)="closed.emit()"></div>
        <div
          [class]="
            cn(
              'relative z-10 h-full w-full max-w-full overflow-y-auto border-l border-border bg-surface shadow-xl md:max-w-lg',
              className()
            )
          "
        >
          <ng-content />
        </div>
      </div>
    }
  `,
})
export class SheetComponent {
  open = input.required<boolean>();
  className = input('');
  closed = output<void>();

  cn = cn;

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.open()) {
      this.closed.emit();
    }
  }
}
