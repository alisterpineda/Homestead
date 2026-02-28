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
    <span
      hlmBadge
      class="tag-badge-colored gap-1"
      [style.--tag-color]="color() ?? '#808080'"
    >
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
  styles: `
    .tag-badge-colored {
      background-color: color-mix(in oklch, var(--tag-color) 15%, transparent);
      color: oklch(from var(--tag-color) min(l, 0.5) c h);
    }

    :host-context(.dark) .tag-badge-colored {
      color: oklch(from var(--tag-color) max(l, 0.75) c h);
    }
  `,
})
export class TagBadgeComponent {
  path = input.required<string>();
  color = input<string | null | undefined>(null);
  removable = input(false);
  removed = output<void>();

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
