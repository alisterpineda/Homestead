import { Component, ChangeDetectionStrategy, inject, signal, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers2, lucideSearch, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { PaginationComponent } from '@/modules/documents/components/pagination/pagination.component';
import { CreateGroupDialogComponent } from '../../components/create-group-dialog/create-group-dialog.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getGroups } from '@/api/generated/fn/admin/get-groups';
import type { PaginatedResponseOfGroupResponse } from '@/api/generated/models/paginated-response-of-group-response';

@Component({
  selector: 'app-group-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmIconImports, HlmSpinner, HlmEmptyImports, HlmInput,
    HlmTableImports, HlmBadge, PaginationComponent, CreateGroupDialogComponent,
  ],
  providers: [provideIcons({ lucideUsers2, lucideSearch, lucidePlus })],
  template: `
    <div>
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideUsers2" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load groups</h3>
            <p hlmEmptyDescription>There was an error loading the group list. Please try again.</p>
          </div>
        </section>
      }

      @if (!loading() || data()) {
        <div class="flex items-center gap-2 mb-4">
          <div class="relative flex-1">
            <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
            <input hlmInput class="pl-9 w-full" placeholder="Search groups..."
              [value]="searchInput()" (input)="searchInput.set(asInputValue($event))" />
          </div>
          <app-create-group-dialog (created)="onRefresh()">
            <ng-icon hlm name="lucidePlus" size="sm" class="mr-1" />
            Create Group
          </app-create-group-dialog>
        </div>

        @if (data(); as d) {
          @if (d.items.length === 0) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideUsers2" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No groups found</h3>
                <p hlmEmptyDescription>Create a group to scope document access.</p>
              </div>
            </section>
          } @else {
            <div class="overflow-hidden rounded-md border">
              <div hlmTableContainer>
                <table hlmTable>
                  <thead hlmTHead>
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

            <div class="mt-4">
              <app-pagination [page]="+d.page" [pageSize]="+d.pageSize" [totalCount]="+d.totalCount"
                [totalPages]="+(d.totalPages ?? 1)" (pageChange)="onPageChange($event)" />
            </div>
          }
        }
      }
    </div>
  `,
})
export class GroupListComponent implements OnDestroy {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

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
    this.route.queryParamMap.pipe(map(params => +(params.get('pageSize') ?? 10))),
    { initialValue: 10 },
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
        return getGroups(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGroupResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  private searchSub: Subscription;

  constructor() {
    this.searchSub = toObservable(this.searchInput)
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      )
      .subscribe(value => {
        this.router.navigate(['/admin/groups'], {
          queryParams: { page: 1, search: value || null },
          queryParamsHandling: 'merge',
        });
      });
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/groups'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onGroupClick(id: string): void {
    this.router.navigate(['/admin/groups', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update(v => v + 1);
  }
}
