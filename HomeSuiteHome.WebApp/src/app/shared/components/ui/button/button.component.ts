import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { cn } from '@/shared/utils';

type ButtonVariant = 'primary' | 'secondary' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg';

const variantStyles: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-primary-foreground hover:bg-primary/90 focus-visible:ring-ring',
  secondary:
    'bg-secondary text-secondary-foreground border border-border hover:bg-secondary/80 focus-visible:ring-ring',
  ghost:
    'text-muted-foreground hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring',
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
