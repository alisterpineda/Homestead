import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmPaginationImports, createPageArray } from '@/shared/components/spartan/pagination';

import { HlmSelectImports } from '@/shared/components/spartan/select';

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, NgIcon, HlmPaginationImports, HlmSelectImports],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  template: `
    @if (totalCount() > 0) {
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 pt-4">
        <span class="text-sm text-muted-foreground">
          Showing {{ rangeStart() }}\u2013{{ rangeEnd() }} of {{ totalCount() }}
        </span>

        <label [for]="pageSizeTriggerId" class="sr-only">Rows per page</label>
        <hlm-select [ngModel]="pageSize()" (ngModelChange)="pageSizeChange.emit(+$event)">
          <hlm-select-trigger class="w-fit" [buttonId]="pageSizeTriggerId">
            <hlm-select-value placeholder="Page size" />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
            <hlm-select-item [value]="10">10 / page</hlm-select-item>
            <hlm-select-item [value]="20">20 / page</hlm-select-item>
            <hlm-select-item [value]="50">50 / page</hlm-select-item>
            <hlm-select-item [value]="100">100 / page</hlm-select-item>
          </hlm-select-group>
</hlm-select-content>
        </hlm-select>

        @if (totalPages() > 1) {
          <nav hlmPagination class="w-full sm:w-auto">
            <ul hlmPaginationContent>
              <!-- Real buttons: the helm pagination links are router links, and without a
                   link they render as href-less anchors — unfocusable, and their aria-label
                   is invalid on a role-less element. buttonVariants only dims on
                   [data-disabled] (set by BrnButton, which hlmPaginationLink lacks), so it is
                   mirrored from the native disabled state here. -->
              <li hlmPaginationItem>
                <button hlmPaginationLink type="button" size="default" class="ps-1.5!"
                        [disabled]="page() <= 1" [attr.data-disabled]="page() <= 1 || null"
                        (click)="pageChange.emit(page() - 1)"
                        aria-label="Go to previous page">
                  <ng-icon name="lucideChevronLeft" class="rtl:rotate-180" />
                  <span class="hidden sm:block">Previous</span>
                </button>
              </li>

              @for (p of pages(); track $index) {
                <li hlmPaginationItem>
                  @if (p === '...') {
                    <hlm-pagination-ellipsis />
                  } @else {
                    <button hlmPaginationLink type="button" [isActive]="page() === p"
                            (click)="pageChange.emit(+p)" [attr.aria-label]="'Page ' + p">
                      {{ p }}
                    </button>
                  }
                </li>
              }

              <li hlmPaginationItem>
                <button hlmPaginationLink type="button" size="default" class="pe-1.5!"
                        [disabled]="page() >= totalPages()" [attr.data-disabled]="page() >= totalPages() || null"
                        (click)="pageChange.emit(page() + 1)"
                        aria-label="Go to next page">
                  <span class="hidden sm:block">Next</span>
                  <ng-icon name="lucideChevronRight" class="rtl:rotate-180" />
                </button>
              </li>
            </ul>
          </nav>
        }
      </div>
    }
  `,
})
export class PaginationComponent {
  private static nextId = 0;
  /** Ties the visually-hidden label to the page-size trigger so it has an accessible name. */
  protected readonly pageSizeTriggerId = `page-size-trigger-${PaginationComponent.nextId++}`;

  page = input.required<number>();
  totalPages = input.required<number>();
  totalCount = input.required<number>();
  pageSize = input.required<number>();

  pageChange = output<number>();
  pageSizeChange = output<number>();

  pages = computed(() =>
    createPageArray(this.page(), this.pageSize(), this.totalCount(), 7),
  );

  rangeStart = computed(() => (this.page() - 1) * this.pageSize() + 1);
  rangeEnd = computed(() => Math.min(this.page() * this.pageSize(), this.totalCount()));
}
