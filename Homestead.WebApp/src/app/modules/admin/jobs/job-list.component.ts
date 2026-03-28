import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideActivity, lucideChevronRight, lucideChevronDown } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmButton } from '@/shared/components/spartan/button';
import { statusBadgeVariant as _statusBadgeVariant, statusLabel as _statusLabel } from '@/shared/utils/processing-status.utils';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { GetProcessingJobsResponse } from '@/api/generated/models/get-processing-jobs-response';
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
    HlmButton,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideActivity, lucideChevronRight, lucideChevronDown })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideActivity" />
            </div>
            <div hlmEmptyTitle>Failed to load jobs</div>
            <div hlmEmptyDescription>There was an error loading the job list. Please try again.</div>
          </div>
        </div>
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
            <option value="PartiallyCompleted">Partially Completed</option>
          </select>
        </div>

        @if (data(); as d) {
          @if (d.items.length === 0 && !statusFilter()) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideActivity" />
                </div>
                <div hlmEmptyTitle>No jobs yet</div>
                <div hlmEmptyDescription>Jobs will appear here once documents are uploaded.</div>
              </div>
            </div>
          } @else if (d.items.length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideActivity" />
                </div>
                <div hlmEmptyTitle>No results</div>
                <div hlmEmptyDescription>No jobs match the selected status filter.</div>
              </div>
            </div>
          } @else {
            <div class="min-h-0 flex-1 overflow-y-auto p-px">
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable>
                    <thead hlmTHead class="sticky top-0 z-10 bg-background">
                      <tr hlmTr>
                        <th hlmTh class="w-8"></th>
                        <th hlmTh>Type</th>
                        <th hlmTh>Status</th>
                        <th hlmTh>Progress</th>
                        <th hlmTh>Created</th>
                        <th hlmTh class="hidden md:table-cell">Started</th>
                        <th hlmTh class="hidden md:table-cell">Completed</th>
                        <th hlmTh class="hidden lg:table-cell">Error</th>
                      </tr>
                    </thead>
                    <tbody hlmTBody>
                      @for (job of d.items; track job.id) {
                        <tr hlmTr>
                          <td hlmTd class="w-8 px-2">
                            @if (isParentJob(job)) {
                              <button
                                hlmBtn
                                variant="ghost"
                                size="icon"
                                class="h-6 w-6"
                                (click)="toggleExpand(job.id)"
                                [attr.aria-expanded]="isExpanded(job.id)"
                                aria-label="Toggle child jobs"
                              >
                                <ng-icon hlm [name]="isExpanded(job.id) ? 'lucideChevronDown' : 'lucideChevronRight'" size="sm" />
                              </button>
                            }
                          </td>
                          <td hlmTd class="font-medium">{{ humanizeType(job.type) }}</td>
                          <td hlmTd>
                            <span hlmBadge [variant]="statusBadgeVariant(job.status)">
                              {{ statusLabel(job.status) }}
                            </span>
                          </td>
                          <td hlmTd class="text-muted-foreground text-sm">
                            @if (isParentJob(job)) {
                              {{ childProgress(job) }}
                            } @else {
                              —
                            }
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
                        @if (isExpanded(job.id)) {
                          @if (childJobsLoading().has(job.id)) {
                            <tr hlmTr>
                              <td hlmTd [attr.colspan]="8" class="py-4 text-center">
                                <hlm-spinner />
                              </td>
                            </tr>
                          }
                          @for (child of getChildJobs(job.id); track child.id) {
                            <tr hlmTr class="bg-muted/50">
                              <td hlmTd class="w-8"></td>
                              <td hlmTd class="font-medium pl-8 text-muted-foreground">{{ humanizeType(child.type) }}</td>
                              <td hlmTd>
                                <span hlmBadge [variant]="statusBadgeVariant(child.status)">
                                  {{ statusLabel(child.status) }}
                                </span>
                              </td>
                              <td hlmTd>—</td>
                              <td hlmTd class="text-muted-foreground text-sm">{{ child.createdAt | date:'medium' }}</td>
                              <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                                {{ child.startedAt ? (child.startedAt | date:'medium') : '—' }}
                              </td>
                              <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                                {{ child.completedAt ? (child.completedAt | date:'medium') : '—' }}
                              </td>
                              <td hlmTd class="hidden lg:table-cell text-muted-foreground text-sm max-w-xs truncate"
                                  [attr.title]="child.error ?? null">
                                {{ child.error ?? '—' }}
                              </td>
                            </tr>
                          }
                        }
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

  private expandedJobs = signal(new Set<string>());
  private childJobsMap = signal(new Map<string, GetProcessingJobsResponse[]>());
  childJobsLoading = signal(new Set<string>());

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

  isParentJob(job: GetProcessingJobsResponse): boolean {
    return +(job.totalChildCount ?? 0) > 0;
  }

  isExpanded(jobId: string): boolean {
    return this.expandedJobs().has(jobId);
  }

  getChildJobs(jobId: string): GetProcessingJobsResponse[] {
    return this.childJobsMap().get(jobId) ?? [];
  }

  toggleExpand(jobId: string): void {
    const expanded = new Set(this.expandedJobs());
    if (expanded.has(jobId)) {
      expanded.delete(jobId);
      this.expandedJobs.set(expanded);
    } else {
      expanded.add(jobId);
      this.expandedJobs.set(expanded);
      this.loadChildJobs(jobId);
    }
  }

  childProgress(job: GetProcessingJobsResponse): string {
    const total = +(job.totalChildCount ?? 0);
    const completed = +(job.completedChildCount ?? 0);
    const failed = +(job.failedChildCount ?? 0);
    const finished = completed + failed;
    let result = `${finished}/${total}`;
    if (failed > 0) {
      result += ` (${failed} failed)`;
    }
    return result;
  }

  private loadChildJobs(parentJobId: string): void {
    const loadingSet = new Set(this.childJobsLoading());
    loadingSet.add(parentJobId);
    this.childJobsLoading.set(loadingSet);

    this.adminApi.getProcessingJobs({
      ParentJobId: parentJobId,
      Page: 1,
      PageSize: 500,
    }).subscribe({
      next: (response) => {
        const newMap = new Map(this.childJobsMap());
        newMap.set(parentJobId, response.items);
        this.childJobsMap.set(newMap);

        const newLoading = new Set(this.childJobsLoading());
        newLoading.delete(parentJobId);
        this.childJobsLoading.set(newLoading);
      },
      error: () => {
        const newLoading = new Set(this.childJobsLoading());
        newLoading.delete(parentJobId);
        this.childJobsLoading.set(newLoading);
      },
    });
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
      case 'ReprocessDocumentVersion': return 'Reprocess Version';
      case 'BulkReprocessDocuments': return 'Bulk Reprocess';
      case 'DownloadDocuments': return 'Download Documents';
      default: return type;
    }
  }

  statusBadgeVariant = _statusBadgeVariant;
  statusLabel = _statusLabel;
}
