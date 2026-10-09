import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest } from 'rxjs';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendarClock, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { CreateScheduleDialogComponent } from './create-schedule-dialog/create-schedule-dialog.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { PaginatedResponseOfGetProcessingSchedulesResponse } from '@/api/generated/models/paginated-response-of-get-processing-schedules-response';
import type { GetProcessingSchedulesResponse } from '@/api/generated/models/get-processing-schedules-response';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { statusBadgeVariant, statusLabel } from '@/shared/utils/processing-status.utils';
import { processingJobTypeLabel } from '@/shared/utils/processing-job-type.utils';

@Component({
  selector: 'app-schedule-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, NgIcon, HlmSpinner, HlmEmptyImports, HlmButton,
    HlmTableImports, HlmBadge, HlmAlertDialogImports,
    PaginationComponent, CreateScheduleDialogComponent,
  ],
  providers: [provideIcons({ lucideCalendarClock, lucidePlus, lucideTrash2 })],
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
              <ng-icon name="lucideCalendarClock" />
            </div>
            <div hlmEmptyTitle>Failed to load schedules</div>
            <div hlmEmptyDescription>There was an error loading the schedule list. Please try again.</div>
          </div>
        </div>
      }

      @if (!loading() || data()) {
        @if (data(); as d) {
          @if (d.items.length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideCalendarClock" />
                </div>
                <div hlmEmptyTitle>No schedules yet</div>
                <div hlmEmptyDescription>Create a No-op schedule to smoke-test the scheduler.</div>
              </div>
              <div hlmEmptyContent>
                <app-create-schedule-dialog (created)="onRefresh()">
                  <ng-icon name="lucidePlus" class="text-[length:--spacing(4)] mr-1" />
                  Create Schedule
                </app-create-schedule-dialog>
              </div>
            </div>
          } @else {
            <div class="flex justify-end mb-4 shrink-0">
              <app-create-schedule-dialog triggerSize="sm" (created)="onRefresh()">
                <ng-icon name="lucidePlus" class="text-[length:--spacing(4)] mr-1" />
                Create Schedule
              </app-create-schedule-dialog>
            </div>

            <div class="min-h-0 flex-1 overflow-y-auto p-px">
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable>
                    <thead hlmTHead class="sticky top-0 z-10 bg-background">
                      <tr hlmTr>
                        <th hlmTh>Name</th>
                        <th hlmTh class="w-32">Job Type</th>
                        <th hlmTh class="w-40">Cron</th>
                        <th hlmTh class="w-24">Enabled</th>
                        <th hlmTh class="hidden md:table-cell">Next Run</th>
                        <th hlmTh class="hidden lg:table-cell">Last Run</th>
                        <th hlmTh class="w-16"></th>
                      </tr>
                    </thead>
                    <tbody hlmTBody>
                      @for (schedule of d.items; track schedule.id) {
                        <tr hlmTr>
                          <td hlmTd class="font-medium">
                            {{ schedule.name }}
                            @if (schedule.description) {
                              <div class="text-xs text-muted-foreground">{{ schedule.description }}</div>
                            }
                          </td>
                          <td hlmTd>
                            <span hlmBadge variant="secondary">{{ humanizeType(schedule.jobType) }}</span>
                          </td>
                          <td hlmTd class="font-mono text-xs">{{ schedule.cronExpression }}</td>
                          <td hlmTd>
                            @if (schedule.isEnabled) {
                              <span hlmBadge variant="default">Enabled</span>
                            } @else {
                              <span hlmBadge variant="outline">Disabled</span>
                            }
                          </td>
                          <td hlmTd class="hidden md:table-cell text-muted-foreground text-sm">
                            {{ schedule.nextRunAt | date:'medium' }}
                          </td>
                          <td hlmTd class="hidden lg:table-cell text-muted-foreground text-sm">
                            @if (schedule.lastRunAt) {
                              <div class="flex items-center gap-2">
                                <span>{{ schedule.lastRunAt | date:'medium' }}</span>
                                @if (schedule.lastRunStatus) {
                                  <span hlmBadge [variant]="statusBadgeVariant(schedule.lastRunStatus)">
                                    {{ statusLabel(schedule.lastRunStatus) }}
                                  </span>
                                }
                              </div>
                            } @else {
                              —
                            }
                          </td>
                          <td hlmTd class="w-16">
                            <hlm-alert-dialog>
                              <button hlmBtn variant="ghost" size="icon" hlmAlertDialogTrigger
                                [attr.aria-label]="'Delete schedule ' + schedule.name">
                                <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
                              </button>
                              <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                                <div hlmAlertDialogHeader>
                                  <h2 hlmAlertDialogTitle>Delete schedule?</h2>
                                  <p hlmAlertDialogDescription>
                                    This will permanently delete <span class="font-medium">{{ schedule.name }}</span>. This action cannot be undone.
                                  </p>
                                </div>
                                <div hlmAlertDialogFooter>
                                  <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                                  <button hlmAlertDialogAction variant="destructive" (click)="onDelete(schedule, ctx)" [disabled]="deletingId() === schedule.id">
                                    {{ deletingId() === schedule.id ? 'Deleting...' : 'Delete' }}
                                  </button>
                                </div>
                              </hlm-alert-dialog-content>
                            </hlm-alert-dialog>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
            <div class="shrink-0">
              <app-pagination [page]="+d.page" [pageSize]="+d.pageSize" [totalCount]="+d.totalCount"
                [totalPages]="+(d.totalPages ?? 1)" (pageChange)="onPageChange($event)" />
            </div>
          }
        }
      }
    </div>
  `,
})
export class ScheduleListComponent {
  private adminApi = inject(AdminApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private errorHandler = inject(ErrorHandlerService);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deletingId = signal<string | null>(null);

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
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.adminApi.getProcessingSchedules({
          Page: page,
          PageSize: pageSize,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetProcessingSchedulesResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  statusBadgeVariant = statusBadgeVariant;
  statusLabel = statusLabel;
  humanizeType = processingJobTypeLabel;

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/schedules'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onDelete(schedule: GetProcessingSchedulesResponse, ctx: { close: () => void }): void {
    this.deletingId.set(schedule.id);

    this.adminApi.deleteProcessingSchedule({ id: schedule.id }).subscribe({
      next: () => {
        this.deletingId.set(null);
        ctx.close();
        toast.success(`Schedule "${schedule.name}" deleted.`);
        this.onRefresh();
      },
      error: (err: unknown) => {
        this.deletingId.set(null);
        ctx.close();
        if (!this.errorHandler.isServerOrNetworkError(err)) {
          this.errorHandler.handleError(err, 'Failed to delete schedule.');
        }
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update(v => v + 1);
  }
}
