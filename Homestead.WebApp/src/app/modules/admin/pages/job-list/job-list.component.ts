import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge, type BadgeVariants } from '@/shared/components/spartan/badge';
import { PaginationComponent } from '@/modules/documents/components/pagination/pagination.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { PaginatedResponseOfGetProcessingJobsResponse } from '@/api/generated/models/paginated-response-of-get-processing-jobs-response';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';
import type { ProcessingJobType } from '@/api/generated/models/processing-job-type';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-job-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    HlmBadge,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideActivity })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideActivity" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load jobs</h3>
            <p hlmEmptyDescription>There was an error loading the job list. Please try again.</p>
          </div>
        </section>
      }

      @if (!loading() || data()) {
        <div class="flex items-center gap-2 mb-4 shrink-0">
          <label for="status-filter" class="text-sm font-medium shrink-0">Status</label>
          <select
            id="status-filter"
            class="h-9 rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            [value]="statusFilter() ?? ''"
            (change)="onStatusChange(asSelectValue($event))"
            aria-label="Filter by status"
          >
            <option value="">All</option>
            <option value="Pending">Pending</option>
            <option value="InProgress">In Progress</option>
            <option value="Completed">Completed</option>
            <option value="Failed">Failed</option>
          </select>
        </div>

        @if (data(); as d) {
          @if (d.items.length === 0 && !statusFilter()) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideActivity" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No jobs yet</h3>
                <p hlmEmptyDescription>Jobs will appear here once documents are uploaded.</p>
              </div>
            </section>
          } @else if (d.items.length === 0) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideActivity" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No results</h3>
                <p hlmEmptyDescription>No jobs match the selected status filter.</p>
              </div>
            </section>
          } @else {
            <div class="min-h-0 flex-1 overflow-y-auto p-px">
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable>
                    <thead hlmTHead class="sticky top-0 z-10 bg-background">
                      <tr hlmTr>
                        <th hlmTh>Type</th>
                        <th hlmTh>Status</th>
                        <th hlmTh>Created</th>
                        <th hlmTh class="hidden md:table-cell">Started</th>
                        <th hlmTh class="hidden md:table-cell">Completed</th>
                        <th hlmTh class="hidden lg:table-cell">Error</th>
                      </tr>
                    </thead>
                    <tbody hlmTBody>
                      @for (job of d.items; track job.id) {
                        <tr hlmTr>
                          <td hlmTd class="font-medium">{{ humanizeType(job.type) }}</td>
                          <td hlmTd>
                            <span hlmBadge [variant]="statusBadgeVariant(job.status)">
                              {{ statusLabel(job.status) }}
                            </span>
                          </td>
                          <td hlmTd class="text-muted-foreground text-sm">{{ job.createdAt | date:'medium' }}</td>
                          <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                            {{ job.startedAt ? (job.startedAt | date:'medium') : '—' }}
                          </td>
                          <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                            {{ job.completedAt ? (job.completedAt | date:'medium') : '—' }}
                          </td>
                          <td hlmTd class="hidden lg:table-cell text-muted-foreground text-sm max-w-xs truncate"
                              [attr.title]="job.error ?? null">
                            {{ job.error ?? '—' }}
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
            <div class="shrink-0">
              <app-pagination
                [page]="+d.page"
                [totalPages]="+(d.totalPages ?? 0)"
                [totalCount]="+d.totalCount"
                [pageSize]="+d.pageSize"
                (pageChange)="onPageChange($event)"
                (pageSizeChange)="onPageSizeChange($event)"
              />
            </div>
          }
        }
      }
    </div>
  `,
})
export class JobListComponent {
  private adminApi = inject(AdminApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  loading = signal(false);
  error = signal(false);

  statusFilter = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('status') as ProcessingJobStatus | null)),
    { initialValue: this.route.snapshot.queryParamMap.get('status') as ProcessingJobStatus | null },
  );

  private page = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('pageSize') ?? 50))),
    { initialValue: 50 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.statusFilter),
    ]).pipe(
      switchMap(([page, pageSize, status]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.adminApi.getProcessingJobs({
          Page: page,
          PageSize: pageSize,
          Status: status ?? undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetProcessingJobsResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  asSelectValue(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  onStatusChange(value: string): void {
    this.router.navigate(['/admin/jobs'], {
      queryParams: { page: 1, status: value || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/jobs'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.router.navigate(['/admin/jobs'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  humanizeType(type: ProcessingJobType): string {
    switch (type) {
      case 'UploadDocument': return 'Upload Document';
      case 'UploadDocumentVersion': return 'Upload Version';
      default: return type;
    }
  }

  statusBadgeVariant(status: ProcessingJobStatus): BadgeVariants['variant'] {
    switch (status) {
      case 'Pending': return 'outline';
      case 'InProgress': return 'default';
      case 'Completed': return 'secondary';
      case 'Failed': return 'destructive';
    }
  }

  statusLabel(status: ProcessingJobStatus): string {
    return status === 'InProgress' ? 'In Progress' : status;
  }
}
