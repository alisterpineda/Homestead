import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { cn } from '@/shared/utils';

type BadgeVariant = 'default' | 'accent';

const variantStyles: Record<BadgeVariant, string> = {
  default: 'bg-surface-raised text-text-secondary border border-border',
  accent: 'bg-accent-100 text-accent-700',
};

@Component({
  selector: 'app-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span [class]="classes()">
      <ng-content />
    </span>
  `,
})
export class BadgeComponent {
  variant = input<BadgeVariant>('default');
  className = input('');

  classes = computed(() =>
    cn(
      'inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium',
      variantStyles[this.variant()],
      this.className(),
    ),
  );
}
