import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucideSparkles, lucideCheck } from '@ng-icons/lucide';
import { HlmBadge } from '@/shared/components/spartan/badge';

@Component({
  selector: 'app-party-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmBadge],
  providers: [provideIcons({ lucideX, lucideSparkles, lucideCheck })],
  styles: `
    :host {
      display: inline-flex;
      min-width: 0;
    }

    .party-badge-ai {
      background-color: color-mix(in oklch, var(--ai) 10%, white);
      color: var(--ai);
      border-color: transparent;
    }

    :host-context(.dark) .party-badge-ai {
      background-color: color-mix(in oklch, var(--ai) 20%, black);
    }
  `,
  template: `
    <span
      hlmBadge
      variant="secondary"
      [class.border-dashed]="aiSuggested()"
      [class.party-badge-ai]="aiSuggested()"
      class="gap-1 max-w-full min-w-0"
    >
      @if (aiSuggested()) {
        <ng-icon name="lucideSparkles" class="text-[length:--spacing(3)] shrink-0" />
      }
      <span class="truncate">{{ name() }}</span>
      @if (aiSuggested() && !aiTransient()) {
        <button
          type="button"
          (click)="onApprove($event)"
          class="ml-0.5 flex h-5 w-5 cursor-pointer items-center justify-center rounded hover:bg-muted-foreground/20"
          aria-label="Approve suggestion"
        >
          <ng-icon name="lucideCheck" class="text-[length:--spacing(3)]" />
        </button>
        <button
          type="button"
          (click)="onDecline($event)"
          class="flex h-5 w-5 cursor-pointer items-center justify-center rounded hover:bg-muted-foreground/20"
          aria-label="Decline suggestion"
        >
          <ng-icon name="lucideX" class="text-[length:--spacing(3)]" />
        </button>
      } @else if ((aiSuggested() && aiTransient()) || removable()) {
        <button
          type="button"
          (click)="onRemove($event)"
          class="ml-0.5 cursor-pointer rounded hover:bg-muted-foreground/20"
          aria-label="Remove party"
        >
          <ng-icon name="lucideX" class="text-[length:--spacing(3)]" />
        </button>
      }
    </span>
  `,
})
export class PartyBadgeComponent {
  name = input.required<string>();
  removable = input(false);
  aiSuggested = input(false);
  aiTransient = input(false);
  removed = output<void>();
  approved = output<void>();
  declined = output<void>();

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
