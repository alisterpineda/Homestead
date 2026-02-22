import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { cn } from '@/shared/utils';

@Component({
  selector: 'app-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div [class]="classes()">
      <ng-content />
    </div>
  `,
})
export class CardComponent {
  className = input('');

  classes = computed(() =>
    cn('rounded-xl border border-border bg-surface p-4 md:p-6', this.className()),
  );
}
