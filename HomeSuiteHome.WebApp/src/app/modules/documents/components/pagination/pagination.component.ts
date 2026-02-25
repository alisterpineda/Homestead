import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/components/ui/icon';
import { HlmButton } from '@/components/ui/button';

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  template: `
    @if (totalPages() > 1) {
      <nav
        aria-label="Pagination"
        class="flex flex-col items-center gap-3 pt-4 sm:flex-row sm:justify-between sm:gap-0"
      >
        <span class="text-sm text-muted-foreground">
          Page {{ page() }} of {{ totalPages() }}
        </span>
        <div class="flex gap-2">
          <button
            hlmBtn
            variant="secondary"
            size="sm"
            [disabled]="page() <= 1"
            (click)="pageChange.emit(page() - 1)"
          >
            <ng-icon hlm name="lucideChevronLeft" size="sm" />
            Previous
          </button>
          <button
            hlmBtn
            variant="secondary"
            size="sm"
            [disabled]="page() >= totalPages()"
            (click)="pageChange.emit(page() + 1)"
          >
            Next
            <ng-icon hlm name="lucideChevronRight" size="sm" />
          </button>
        </div>
      </nav>
    }
  `,
})
export class PaginationComponent {
  page = input.required<number>();
  totalPages = input.required<number>();
  pageChange = output<number>();
}
