import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import type { LucideIconData } from 'lucide-angular';

@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  template: `
    <div class="flex flex-col items-center justify-center px-4 py-12 text-center md:py-16">
      @if (icon()) {
        <div class="mb-4 rounded-full bg-surface-raised p-3">
          <lucide-icon [img]="icon()!" [size]="24" class="text-text-muted" />
        </div>
      }
      <h3 class="text-lg font-medium text-text-primary">{{ title() }}</h3>
      @if (description()) {
        <p class="mt-1 text-sm text-text-secondary">{{ description() }}</p>
      }
      <div class="mt-4">
        <ng-content />
      </div>
    </div>
  `,
})
export class EmptyStateComponent {
  icon = input<LucideIconData | null>(null);
  title = input.required<string>();
  description = input<string>('');
}
