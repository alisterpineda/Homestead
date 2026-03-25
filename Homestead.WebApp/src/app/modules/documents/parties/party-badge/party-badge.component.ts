import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmIconImports } from '@/shared/components/spartan/icon';

@Component({
  selector: 'app-party-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmBadge],
  providers: [provideIcons({ lucideX })],
  styles: `
    :host {
      display: inline-flex;
      min-width: 0;
    }
  `,
  template: `
    <span hlmBadge variant="secondary" class="gap-1 max-w-full min-w-0">
      <span class="truncate">{{ name() }}</span>
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
export class PartyBadgeComponent {
  name = input.required<string>();
  removable = input(false);
  removed = output<void>();

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
