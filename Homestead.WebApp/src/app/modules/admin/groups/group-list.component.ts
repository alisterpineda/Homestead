import { Component, ChangeDetectionStrategy, inject, signal, DestroyRef } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers2, lucidePlus } from '@ng-icons/lucide';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { CreateGroupDialogComponent } from './create-group-dialog/create-group-dialog.component';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { PaginatedResponseOfGroupResponse } from '@/api/generated/models/paginated-response-of-group-response';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';

@Component({
  selector: 'app-group-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmSpinner, HlmEmptyImports, SearchInputComponent,
    HlmTableImports, HlmBadge, PaginationComponent, CreateGroupDialogComponent,
  ],
  providers: [provideIcons({ lucideUsers2, lucidePlus })],
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
              <ng-icon name="lucideUsers2" />
            </div>
            <div hlmEmptyTitle>Failed to load groups</div>
            <div hlmEmptyDescription>There was an error loading the group list. Please try again.</div>
          </div>
        </div>
      }

      @if (!loading() || data()) {
        <div class="flex items-center gap-2 mb-4 shrink-0">
          <app-search-input class="flex-1" [(value)]="searchInput" placeholder="Search groups..." ariaLabel="Search groups" />
          <app-create-group-dialog (created)="onRefresh()">
            <ng-icon name="lucidePlus" class="text-[length:--spacing(4)] mr-1" />
            Create Group
          </app-create-group-dialog>
        </div>

        @if (data(); as d) {
          @if (d.items.length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideUsers2" />
                </div>
                <div hlmEmptyTitle>No groups found</div>
                <div hlmEmptyDescription>Create a group to scope document access.</div>
              </div>
            </div>
          } @else {
            <div class="min-h-0 flex-1 overflow-y-auto p-px">
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable>
                    <thead hlmTHead class="sticky top-0 z-10 bg-background">
                      <tr hlmTr>
                        <th hlmTh>Name</th>
                        <th hlmTh class="hidden md:table-cell">Description</th>
                        <th hlmTh class="w-28 text-center">Members</th>
                      </tr>
                    </thead>
                    <tbody hlmTBody>
                      @for (group of d.items; track group.id) {
                        <tr hlmTr tabindex="0" (click)="onGroupClick(group.id)" class="cursor-pointer">
                          <td hlmTd class="font-medium">{{ group.name }}</td>
                          <td hlmTd class="hidden md:table-cell text-muted-foreground">{{ group.description ?? '—' }}</td>
                          <td hlmTd class="text-center">
                            <span hlmBadge variant="secondary">{{ group.memberCount }}</span>
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
export class GroupListComponent {
  private adminApi = inject(AdminApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
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
      toObservable(this.searchTerm),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return this.adminApi.getGroups({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r as PaginatedResponseOfGroupResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  constructor() {
    toObservable(this.searchInput)
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(value => {
        this.router.navigate(['/admin/groups'], {
          queryParams: { page: 1, search: value || null },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      });
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/groups'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onGroupClick(id: string): void {
    this.router.navigate(['/admin/groups', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update(v => v + 1);
  }
}
