import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { X } from 'lucide-angular';
import { BadgeComponent } from '@/shared/components/ui/badge/badge.component';

@Component({
  selector: 'app-party-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, BadgeComponent],
  template: `
    <app-badge variant="accent" className="gap-1">
      {{ name() }}
      @if (removable()) {
        <button
          type="button"
          (click)="onRemove($event)"
          class="ml-0.5 rounded hover:bg-accent-200 cursor-pointer"
        >
          <lucide-icon [img]="xIcon" [size]="12" />
        </button>
      }
    </app-badge>
  `,
})
export class PartyBadgeComponent {
  name = input.required<string>();
  removable = input(false);
  removed = output<void>();

  xIcon = X;

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
