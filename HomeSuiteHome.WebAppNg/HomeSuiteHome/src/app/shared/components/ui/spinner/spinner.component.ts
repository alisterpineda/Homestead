import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import { cn } from '@/shared/utils';

const sizeStyles: Record<string, string> = {
  sm: 'h-4 w-4',
  md: 'h-6 w-6',
  lg: 'h-8 w-8',
};

@Component({
  selector: 'app-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [class]="classes()" viewBox="0 0 24 24" fill="none">
      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
      <path
        class="opacity-75"
        fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
      />
    </svg>
  `,
})
export class SpinnerComponent {
  size = input<'sm' | 'md' | 'lg'>('md');
  className = input<string>('');

  classes = computed(() =>
    cn('animate-spin text-accent-500', sizeStyles[this.size()], this.className()),
  );
}
