import { Component, ChangeDetectionStrategy, inject, signal, DestroyRef } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucidePackage } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { statusBadgeVariant, statusLabel } from '@/shared/utils/processing-status.utils';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { ExportApiService } from '@/api/services/export-api.service';
import type { PaginatedResponseOfGetExportsResponse } from '@/api/generated/models/paginated-response-of-get-exports-response';
import type { ProcessingJobStatus } from '@/api/generated/models/processing-job-status';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { formatBytes } from '@/shared/utils';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';

@Component({
  selector: 'app-export-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    HlmButton,
    HlmBadge,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideDownload, lucidePackage })],
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
            <ng-icon hlm name="lucidePackage" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load exports</h3>
            <p hlmEmptyDescription>There was an error loading the export list. Please try again.</p>
          </div>
        </section>
      }

      @if (!loading() || data()) {
        @if (data(); as d) {
          @if (d.items.length === 0) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucidePackage" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No exports yet</h3>
                <p hlmEmptyDescription>Exports will appear here when you export documents.</p>
              </div>
            </section>
          } @else {
            <div class="min-h-0 flex-1 overflow-y-auto p-px">
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable>
                    <thead hlmTHead class="sticky top-0 z-10 bg-background">
                      <tr hlmTr>
                        <th hlmTh>Status</th>
                        <th hlmTh>Documents</th>
                        <th hlmTh class="hidden sm:table-cell">Size</th>
                        <th hlmTh>Created</th>
                        <th hlmTh class="hidden md:table-cell">Completed</th>
                        <th hlmTh>Actions</th>
                      </tr>
                    </thead>
                    <tbody hlmTBody>
                      @for (exp of d.items; track exp.id) {
                        <tr hlmTr>
                          <td hlmTd>
                            <span hlmBadge [variant]="statusBadgeVariant(exp.status)">
                              {{ statusLabel(exp.status) }}
                            </span>
                          </td>
                          <td hlmTd class="text-sm">{{ exp.documentCount ?? '—' }}</td>
                          <td hlmTd class="hidden sm:table-cell text-muted-foreground text-sm">
                            {{ exp.zipFileSize ? formatSize(+exp.zipFileSize) : '—' }}
                          </td>
                          <td hlmTd class="text-muted-foreground text-sm">{{ exp.createdAt | date:'medium' }}</td>
                          <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                            {{ exp.completedAt ? (exp.completedAt | date:'medium') : '—' }}
                          </td>
                          <td hlmTd>
                            @if (exp.status === 'Completed' && !isExpired(exp.expiresAt)) {
                              <a hlmBtn variant="outline" size="icon-sm" [href]="getDownloadUrl(exp.id)" aria-label="Download export">
                                <ng-icon hlm name="lucideDownload" size="sm" />
                              </a>
                            } @else if (exp.status === 'Completed' && isExpired(exp.expiresAt)) {
                              <span class="text-sm text-muted-foreground">Expired</span>
                            } @else if (exp.status === 'Failed') {
                              <span class="text-sm text-destructive truncate max-w-[200px] inline-block" [attr.title]="exp.error ?? null">
                                {{ exp.error ?? 'Unknown error' }}
                              </span>
                            } @else {
                              <span class="text-sm text-muted-foreground">Processing...</span>
                            }
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
export class ExportListComponent {
  private exportApi = inject(ExportApiService);
  private processingHub = inject(ProcessingHubService);
  private destroyRef = inject(DestroyRef);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  loading = signal(false);
  error = signal(false);
  private refreshTrigger = signal(0);

  private page = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map(params => +(params.get('pageSize') ?? 20))),
    { initialValue: 20 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.exportApi.getExports({
          Page: page,
          PageSize: pageSize,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetExportsResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  constructor() {
    this.processingHub.jobCompleted$.pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => {
      this.refreshTrigger.update(v => v + 1);
    });
  }

  onPageChange(newPage: number): void {
    this.router.navigate([], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.router.navigate([], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  getDownloadUrl(jobId: string): string {
    return this.exportApi.getDownloadUrl(jobId);
  }

  formatSize(bytes: number): string {
    return formatBytes(bytes);
  }

  isExpired(expiresAt?: string | null): boolean {
    if (!expiresAt) return false;
    return new Date(expiresAt) < new Date();
  }

  statusBadgeVariant = statusBadgeVariant;
  statusLabel = statusLabel;
}
