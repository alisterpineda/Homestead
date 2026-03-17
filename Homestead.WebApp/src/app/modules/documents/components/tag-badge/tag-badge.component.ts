import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
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
      [variant]="color() ? 'default' : 'secondary'"
      [class.tag-badge-colored]="color()"
      class="badge-inner gap-1"
      [style.--tag-color]="color()"
    >
      <span class="truncate min-w-0">{{ compact() ? compactPath() : path() }}</span>
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
    :host {
      display: inline-flex;
      min-width: 0;
    }

    .badge-inner {
      max-width: 100%;
      min-width: 0;
      flex-shrink: 1;
    }

    .tag-badge-colored {
      background-color: color-mix(in oklch, var(--tag-color) 15%, white);
      color: oklch(from var(--tag-color) min(l, 0.5) c h);
    }

    :host-context(.dark) .tag-badge-colored {
      background-color: color-mix(in oklch, var(--tag-color) 15%, black);
      color: oklch(from var(--tag-color) max(l, 0.75) c h);
    }
  `,
})
export class TagBadgeComponent {
  path = input.required<string>();
  color = input<string | null | undefined>(null);
  removable = input(false);
  compact = input(false);
  removed = output<void>();

  compactPath = computed(() => {
    const segments = this.path().split('/');
    if (segments.length <= 2) return this.path();
    return `${segments[0]}/\u2026/${segments[segments.length - 1]}`;
  });

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
