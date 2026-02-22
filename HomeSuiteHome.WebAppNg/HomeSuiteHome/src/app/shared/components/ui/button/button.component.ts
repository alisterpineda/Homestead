import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { cn } from '@/shared/utils';

type ButtonVariant = 'primary' | 'secondary' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg';

const variantStyles: Record<ButtonVariant, string> = {
  primary: 'bg-accent-500 text-white hover:bg-accent-600 focus-visible:ring-accent-500',
  secondary:
    'bg-surface-raised text-text-primary border border-border hover:bg-surface-overlay focus-visible:ring-accent-500',
  ghost:
    'text-text-secondary hover:bg-surface-raised hover:text-text-primary focus-visible:ring-accent-500',
};

const sizeStyles: Record<ButtonSize, string> = {
  sm: 'px-2.5 py-1.5 text-sm',
  md: 'px-4 py-2 text-sm',
  lg: 'px-5 py-2.5 text-base',
};

@Component({
  selector: 'app-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button [class]="classes()" [disabled]="disabled()" [type]="type()">
      <ng-content />
    </button>
  `,
})
export class ButtonComponent {
  variant = input<ButtonVariant>('primary');
  size = input<ButtonSize>('md');
  disabled = input(false);
  className = input('');
  type = input<'button' | 'submit' | 'reset'>('button');

  classes = computed(() =>
    cn(
      'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 cursor-pointer',
      variantStyles[this.variant()],
      sizeStyles[this.size()],
      this.disabled() && 'pointer-events-none opacity-50',
      this.className(),
    ),
  );
}
