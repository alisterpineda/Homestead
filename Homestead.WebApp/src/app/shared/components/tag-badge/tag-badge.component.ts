import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucideSparkles, lucideCheck } from '@ng-icons/lucide';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmIconImports } from '@/shared/components/spartan/icon';

@Component({
  selector: 'app-tag-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmBadge],
  providers: [provideIcons({ lucideX, lucideSparkles, lucideCheck })],
  template: `
    <span
      hlmBadge
      [variant]="color() && !aiSuggested() ? 'default' : 'secondary'"
      [class.tag-badge-colored]="color() && !aiSuggested()"
      [class.border-dashed]="aiSuggested()"
      [class.tag-badge-ai]="aiSuggested()"
      class="badge-inner gap-1"
      [style.--tag-color]="color()"
    >
      @if (aiSuggested()) {
        <ng-icon hlm name="lucideSparkles" class="shrink-0" size="xs" />
      }
      <span class="truncate min-w-0">{{ compact() ? compactPath() : path() }}</span>
      @if (aiSuggested() && !aiTransient()) {
        <button
          type="button"
          (click)="onApprove($event)"
          class="ml-0.5 flex h-5 w-5 cursor-pointer items-center justify-center rounded hover:bg-muted-foreground/20"
          aria-label="Approve suggestion"
        >
          <ng-icon hlm name="lucideCheck" size="xs" />
        </button>
        <button
          type="button"
          (click)="onDecline($event)"
          class="flex h-5 w-5 cursor-pointer items-center justify-center rounded hover:bg-muted-foreground/20"
          aria-label="Decline suggestion"
        >
          <ng-icon hlm name="lucideX" size="xs" />
        </button>
      } @else if ((aiSuggested() && aiTransient()) || removable()) {
        <button
          type="button"
          (click)="onRemove($event)"
          class="ml-0.5 cursor-pointer rounded hover:bg-muted-foreground/20"
          aria-label="Remove tag"
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

    .tag-badge-ai {
      background-color: color-mix(in oklch, var(--ai) 10%, white);
      color: var(--ai);
      border-color: transparent;
    }

    :host-context(.dark) .tag-badge-ai {
      background-color: color-mix(in oklch, var(--ai) 20%, black);
    }
  `,
})
export class TagBadgeComponent {
  path = input.required<string>();
  color = input<string | null | undefined>(null);
  removable = input(false);
  compact = input(false);
  aiSuggested = input(false);
  aiTransient = input(false);
  removed = output<void>();
  approved = output<void>();
  declined = output<void>();

  compactPath = computed(() => {
    const segments = this.path().split('/');
    if (segments.length <= 2) return this.path();
    return `${segments[0]}/…/${segments[segments.length - 1]}`;
  });

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }

  onApprove(event: Event): void {
    event.stopPropagation();
    this.approved.emit();
  }

  onDecline(event: Event): void {
    event.stopPropagation();
    this.declined.emit();
  }
}
