import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucideSearch } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { UserTableComponent } from '../../components/user-table/user-table.component';
import { PaginationComponent } from '@/modules/documents/components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getUsers } from '@/api/generated/fn/admin/get-users';
import type { PaginatedResponseOfGetUsersResponse } from '@/api/generated/models/paginated-response-of-get-users-response';

@Component({
  selector: 'app-user-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    UserTableComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucideSearch })],
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
            <ng-icon hlm name="lucideUsers" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load users</h3>
            <p hlmEmptyDescription>There was an error loading the user list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        @if (result.items.length === 0 && !searchTerm()) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideUsers" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No users yet</h3>
              <p hlmEmptyDescription>No registered users found.</p>
            </div>
          </section>
        } @else {
          <div class="mb-4">
            <div class="relative">
              <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
              <input
                hlmInput
                type="text"
                placeholder="Search users..."
                class="pl-9"
                [value]="searchInput()"
                (input)="searchInput.set($any($event.target).value)"
                aria-label="Search users"
              />
            </div>
          </div>

          @if (result.items.length === 0) {
            <section hlmEmpty>
              <hlm-empty-media variant="icon">
                <ng-icon hlm name="lucideSearch" size="xl" />
              </hlm-empty-media>
              <div hlmEmptyHeader>
                <h3 hlmEmptyTitle>No results found</h3>
                <p hlmEmptyDescription>No users match your search. Try a different term.</p>
              </div>
            </section>
          } @else {
            <app-user-table [users]="result.items" />
            <app-pagination
              [page]="+result.page"
              [totalPages]="+(result.totalPages ?? 0)"
              [totalCount]="+result.totalCount"
              [pageSize]="+result.pageSize"
              (pageChange)="onPageChange($event)"
              (pageSizeChange)="onPageSizeChange($event)"
            />
          }
        }
      }
    </div>
  `,
})
export class UserListComponent implements OnDestroy {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  loading = signal(false);
  error = signal(false);

  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? 10))),
    { initialValue: 10 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.searchTerm),
    ]).pipe(
      switchMap(([page, pageSize, search]) => {
        this.loading.set(true);
        this.error.set(false);
        return getUsers(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetUsersResponse;
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
        filter((value) => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      )
      .subscribe((value) => {
        this.router.navigate(['/admin/users'], {
          queryParams: { page: 1, search: value || null },
          queryParamsHandling: 'merge',
        });
      });
  }

  ngOnDestroy(): void {
    this.searchSub.unsubscribe();
  }

  onPageChange(newPage: number): void {
    this.router.navigate(['/admin/users'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onPageSizeChange(newSize: number): void {
    this.router.navigate(['/admin/users'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
    });
  }
}
