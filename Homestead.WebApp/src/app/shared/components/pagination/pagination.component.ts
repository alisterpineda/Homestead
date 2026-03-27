import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmPaginationImports, createPageArray } from '@/shared/components/spartan/pagination';

import { HlmSelectImports } from '@/shared/components/spartan/select';

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, HlmPaginationImports, HlmSelectImports],
  template: `
    @if (totalCount() > 0) {
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 pt-4">
        <span class="text-sm text-muted-foreground">
          Showing {{ rangeStart() }}\u2013{{ rangeEnd() }} of {{ totalCount() }}
        </span>

        <hlm-select [ngModel]="pageSize()" (ngModelChange)="pageSizeChange.emit(+$event)">
          <hlm-select-trigger class="w-fit">
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
              <li hlmPaginationItem
                  [class.pointer-events-none]="page() <= 1"
                  [class.opacity-50]="page() <= 1"
                  (click)="pageChange.emit(page() - 1)">
                <hlm-pagination-previous />
              </li>

              @for (p of pages(); track $index) {
                <li hlmPaginationItem>
                  @if (p === '...') {
                    <hlm-pagination-ellipsis />
                  } @else {
                    <a hlmPaginationLink [isActive]="page() === p" (click)="pageChange.emit(+p)">
                      {{ p }}
                    </a>
                  }
                </li>
              }

              <li hlmPaginationItem
                  [class.pointer-events-none]="page() >= totalPages()"
                  [class.opacity-50]="page() >= totalPages()"
                  (click)="pageChange.emit(page() + 1)">
                <hlm-pagination-next />
              </li>
            </ul>
          </nav>
        }
      </div>
    }
  `,
})
export class PaginationComponent {
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
