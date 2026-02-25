import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmIconImports } from '@/shared/components/spartan/icon';

@Component({
  selector: 'app-tag-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmBadge],
  providers: [provideIcons({ lucideX })],
  template: `
    <span hlmBadge class="gap-1">
      {{ path() }}
      @if (removable()) {
        <button
          type="button"
          (click)="onRemove($event)"
          class="ml-0.5 cursor-pointer rounded hover:bg-muted-foreground/20"
        >
          <ng-icon hlm name="lucideX" size="xs" />
        </button>
      }
    </span>
  `,
})
export class TagBadgeComponent {
  path = input.required<string>();
  removable = input(false);
  removed = output<void>();

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
