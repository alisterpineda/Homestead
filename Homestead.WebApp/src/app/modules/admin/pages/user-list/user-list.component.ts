import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucideSearch, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { UserTableComponent } from '../../components/user-table/user-table.component';
import { CreateUserDialogComponent } from '../../components/create-user-dialog/create-user-dialog.component';
import { EditUserSheetComponent } from '../../components/edit-user-sheet/edit-user-sheet.component';
import { PaginationComponent } from '@/modules/documents/components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getUsers } from '@/api/generated/fn/admin/get-users';
import { bulkDeleteUsers } from '@/api/generated/fn/admin/bulk-delete-users';
import type { UserResponse } from '@/api/generated/models/user-response';
import type { PaginatedResponseOfUserResponse } from '@/api/generated/models/paginated-response-of-user-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-user-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmAlertDialogImports,
    UserTableComponent,
    CreateUserDialogComponent,
    EditUserSheetComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucideSearch, lucidePlus, lucideTrash2 })],
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
              <p hlmEmptyDescription>Create your first user to get started.</p>
            </div>
            <app-create-user-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New User
            </app-create-user-dialog>
          </section>
        } @else {
          <div class="relative">
            <!-- Normal toolbar -->
            <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()">
              <div class="flex items-center gap-2 mb-4">
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
                <div class="ml-auto flex items-center gap-2">
                  <app-create-user-dialog triggerSize="sm" (created)="onRefresh()">
                    <ng-icon hlm name="lucidePlus" size="sm" />
                    New User
                  </app-create-user-dialog>
                </div>
              </div>
            </div>

            <!-- Selection toolbar -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection()" [class.pointer-events-none]="!hasSelection()">
              <span class="text-sm text-muted-foreground">
                {{ selectionCount() }} {{ selectionCount() > 1 ? 'users' : 'user' }} selected
              </span>
              <div class="ml-auto flex items-center gap-2">
                <button hlmBtn variant="outline" size="sm" (click)="clearSelection()">
                  Clear selection
                </button>
                <hlm-alert-dialog>
                  <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                  <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                    <div hlmAlertDialogHeader>
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} {{ selectionCount() > 1 ? 'users' : 'user' }}?</h2>
                      <p hlmAlertDialogDescription>
                        This action cannot be undone. The selected {{ selectionCount() > 1 ? 'users' : 'user' }} will be permanently deleted.
                      </p>
                    </div>
                    <div hlmAlertDialogFooter>
                      <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                      <button hlmAlertDialogAction variant="destructive" (click)="onBulkDelete(ctx)" [disabled]="deleting()">
                        {{ deleting() ? 'Deleting...' : 'Delete' }}
                      </button>
                    </div>
                  </hlm-alert-dialog-content>
                </hlm-alert-dialog>
              </div>
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
            <app-user-table
              [users]="result.items"
              [selectedIds]="selectedUserIds()"
              (userSelect)="onUserSelect($event)"
              (selectionChange)="selectedUserIds.set($event)"
            />
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

    <app-edit-user-sheet
      [user]="selectedUser()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class UserListComponent implements OnDestroy {
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);

  // Selection state
  selectedUserIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedUserIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Sheet state
  selectedUser = signal<UserResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

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
      toObservable(this.refreshTrigger),
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
            return r.body as PaginatedResponseOfUserResponse;
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

  onUserSelect(id: string): void {
    const users = this.data()?.items;
    const user = users?.find((u) => u.id === id) ?? null;
    if (user) {
      this.selectedUser.set(user);
      this.sheetState.set('open');
    }
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedUser.set(null);
  }

  clearSelection(): void {
    this.selectedUserIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedUserIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteUsers(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} ${count > 1 ? 'users' : 'user'} deleted.`);
        this.clearSelection();
        this.onRefresh();
      },
      error: (err) => {
        this.deleting.set(false);
        const message = err?.error ?? 'Failed to delete users. Please try again.';
        toast.error(typeof message === 'string' ? message : 'Failed to delete users. Please try again.');
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
