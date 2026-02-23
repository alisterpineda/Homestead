import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { ChevronLeft, ChevronRight } from 'lucide-angular';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, ButtonComponent],
  template: `
    @if (totalPages() > 1) {
      <div
        class="flex flex-col items-center gap-3 pt-4 sm:flex-row sm:justify-between sm:gap-0"
      >
        <span class="text-sm text-muted-foreground">
          Page {{ page() }} of {{ totalPages() }}
        </span>
        <div class="flex gap-2">
          <app-button
            variant="secondary"
            size="sm"
            [disabled]="page() <= 1"
            (click)="pageChange.emit(page() - 1)"
          >
            <lucide-icon [img]="chevronLeftIcon" [size]="16" />
            Previous
          </app-button>
          <app-button
            variant="secondary"
            size="sm"
            [disabled]="page() >= totalPages()"
            (click)="pageChange.emit(page() + 1)"
          >
            Next
            <lucide-icon [img]="chevronRightIcon" [size]="16" />
          </app-button>
        </div>
      </div>
    }
  `,
})
export class PaginationComponent {
  page = input.required<number>();
  totalPages = input.required<number>();
  pageChange = output<number>();

  chevronLeftIcon = ChevronLeft;
  chevronRightIcon = ChevronRight;
}
