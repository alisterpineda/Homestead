import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { X } from 'lucide-angular';
import { BadgeComponent } from '@/shared/components/ui/badge/badge.component';

@Component({
  selector: 'app-tag-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, BadgeComponent],
  template: `
    <app-badge className="gap-1">
      {{ path() }}
      @if (removable()) {
        <button
          type="button"
          (click)="onRemove($event)"
          class="ml-0.5 rounded hover:bg-surface-overlay cursor-pointer"
        >
          <lucide-icon [img]="xIcon" [size]="12" />
        </button>
      }
    </app-badge>
  `,
})
export class TagBadgeComponent {
  path = input.required<string>();
  removable = input(false);
  removed = output<void>();

  xIcon = X;

  onRemove(event: Event): void {
    event.stopPropagation();
    this.removed.emit();
  }
}
