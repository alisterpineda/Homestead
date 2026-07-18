import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { AuthService } from '@/shared/services/auth.service';
import { Router, ActivatedRoute, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, combineLatest, debounceTime, distinctUntilChanged, filter, take } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideContact,
  lucideCircleAlert,
  lucideArrowUp,
  lucideArrowDown,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { PayeeListStateService } from './payee-list-state.service';
import { PayeeTableComponent, type PayeeSortChange } from './payee-table/payee-table.component';
import { CreatePayeeDialogComponent } from './create-payee-dialog/create-payee-dialog.component';
import { EditPayeeDialogComponent } from './edit-payee-dialog/edit-payee-dialog.component';
import { PayeePickerComponent } from '../shared/payee-picker/payee-picker.component';
import type { PayeeResponse } from '@/api/generated/models/payee-response';
import type { PaginatedResponseOfPayeeResponse } from '@/api/generated/models/paginated-response-of-payee-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface PayeeSortFieldOption {
  readonly value: string;
  readonly label: string;
  readonly defaultDesc: boolean;
}

@Component({
  selector: 'app-payee-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    SearchInputComponent,
    HlmButton,
    HlmCheckboxImports,
    HlmLabel,
    HlmAlertDialogImports,
    HlmDialogImports,
    HlmDropdownMenuImports,
    PaginationComponent,
    PayeeTableComponent,
    CreatePayeeDialogComponent,
    EditPayeeDialogComponent,
    PayeePickerComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucideContact,
    lucideCircleAlert,
    lucideArrowUp,
    lucideArrowDown,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col p-4 md:p-6">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Failed to load payees</div>
            <div hlmEmptyDescription>There was an error loading the payee list. Please try again.</div>
          </div>
        </div>
      }

      @if (data(); as result) {
        <!-- The toolbar (with the "Include archived" toggle) stays visible whenever data is loaded — even
             when the active list is empty — so archiving your last payee never strands the archived rows
             out of reach. Only the table/pagination and the hero copy react to emptiness. -->
        <div class="mb-3 flex flex-wrap items-center gap-2 shrink-0">
          <app-search-input class="flex-1 min-w-0" [(value)]="searchInput" placeholder="Search payees..." ariaLabel="Search payees" />
          <label class="flex items-center gap-2 text-sm whitespace-nowrap" hlmLabel>
            <hlm-checkbox
              [checked]="includeArchived()"
              (checkedChange)="onIncludeArchivedChange($event === true)"
              [aria-label]="'Include archived payees'"
            />
            Include archived
          </label>
          <button hlmBtn variant="outline" [hlmDropdownMenuTrigger]="sortMenu">
            <ng-icon hlm [name]="effectiveSortDir() === 'asc' ? 'lucideArrowUp' : 'lucideArrowDown'" size="sm" />
            {{ sortLabel() }}
          </button>
          <ng-template #sortMenu>
            <hlm-dropdown-menu class="w-44">
              <span hlmDropdownMenuLabel>Sort by</span>
              <hlm-dropdown-menu-group>
                @for (opt of sortFields; track opt.value) {
                  <button hlmDropdownMenuRadio [checked]="effectiveSortBy() === opt.value" (triggered)="onSortByChange(opt.value)">
                    <hlm-dropdown-menu-radio-indicator />
                    {{ opt.label }}
                  </button>
                }
              </hlm-dropdown-menu-group>
              <hlm-dropdown-menu-separator />
              <span hlmDropdownMenuLabel>Direction</span>
              <hlm-dropdown-menu-group>
                <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'asc'" (triggered)="onSortDirChange('asc')">
                  <hlm-dropdown-menu-radio-indicator />
                  Ascending
                </button>
                <button hlmDropdownMenuRadio [checked]="effectiveSortDir() === 'desc'" (triggered)="onSortDirChange('desc')">
                  <hlm-dropdown-menu-radio-indicator />
                  Descending
                </button>
              </hlm-dropdown-menu-group>
            </hlm-dropdown-menu>
          </ng-template>
          @if (auth.canWrite()) {
            <app-create-payee-dialog (created)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New payee
            </app-create-payee-dialog>
          }
        </div>

        <div hlmEmpty [class.hidden]="!showInitialEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideContact" />
            </div>
            <div hlmEmptyTitle>No payees yet</div>
            <div hlmEmptyDescription>Use "New payee" above, or create one on the fly while recording an expense or income. Toggle "Include archived" to see archived payees.</div>
          </div>
        </div>

        <div hlmEmpty [class.hidden]="!showFilteredEmpty()">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideContact" />
            </div>
            <div hlmEmptyTitle>No matching payees</div>
            <div hlmEmptyDescription>Try adjusting your search to find what you're looking for.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" size="sm" (click)="resetSearch()">
              Reset search
            </button>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-y-auto p-px" [class.hidden]="!hasItems()">
          <app-payee-table
            [payees]="result.items"
            [sortBy]="effectiveSortBy()"
            [sortDir]="effectiveSortDir()"
            (sortChange)="onSortChange($event)"
            (edit)="onEdit($event)"
            (merge)="onMergeRequest($event)"
            (delete)="onDeleteRequest($event)"
          />
        </div>
        <div class="shrink-0" [class.hidden]="!hasItems()">
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            [totalCount]="+result.totalCount"
            [pageSize]="+result.pageSize"
            (pageChange)="onPageChange($event)"
            (pageSizeChange)="onPageSizeChange($event)"
          />
        </div>
      }
    </div>

    <app-edit-payee-dialog
      [payee]="selectedPayee()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="onEditClosed()"
    />

    <!-- Merge: consolidate the chosen source payee into a target the reviewer keeps. -->
    <hlm-dialog [state]="mergeState()" (stateChanged)="onMergeStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[420px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Merge {{ mergeSource()?.name ?? 'payee' }}</h2>
          <p hlmDialogDescription>
            Move this payee's transactions and learned mappings into another payee, then delete it. This can't
            be undone.
          </p>
        </div>
        <div class="flex flex-col gap-1.5 py-2">
          <label hlmLabel>Merge into</label>
          <app-payee-picker
            [value]="mergeTarget()"
            placeholder="Choose the payee to keep…"
            ariaLabel="Choose the payee to keep"
            (valueChange)="mergeTarget.set($event)"
          />
        </div>
        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
          <button hlmBtn type="button" [disabled]="!canMerge() || merging()" (click)="confirmMerge()">
            {{ merging() ? 'Merging…' : 'Merge' }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'payee' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the payee. A payee in use by any transaction cannot be deleted —
            archive it instead.
          </p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button
            hlmAlertDialogAction
            variant="destructive"
            (click)="confirmDelete(ctx)"
            [disabled]="deleting()"
          >
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class PayeeListComponent {
  auth = inject(AuthService);
  private payeeApi = inject(FinancesPayeeApiService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private listState = inject(PayeeListStateService);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);

  readonly sortFields: readonly PayeeSortFieldOption[] = [
    { value: 'name', label: 'Name', defaultDesc: false },
    { value: 'transactions', label: 'Transactions', defaultDesc: true },
    { value: 'updated', label: 'Updated', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  ready = signal(true);

  // Edit dialog
  selectedPayee = signal<PayeeResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  // Delete confirm
  pendingDelete = signal<PayeeResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  deleteError = signal('');

  // Merge
  mergeSource = signal<PayeeResponse | null>(null);
  mergeTarget = signal<PayeeResponse | null>(null);
  mergeState = signal<BrnDialogState>('closed');
  merging = signal(false);
  canMerge = computed(() => {
    const source = this.mergeSource();
    const target = this.mergeTarget();
    return !!source && !!target && source.id !== target.id;
  });

  // Search
  searchInput = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  searchTerm = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('search') ?? '')),
    { initialValue: this.route.snapshot.queryParamMap.get('search') ?? '' },
  );

  // Sort state from query params
  sortBy = toSignal(this.route.queryParamMap.pipe(map(p => p.get('sortBy'))));
  sortDir = toSignal(this.route.queryParamMap.pipe(map(p => p.get('sortDir'))));

  // Archived filter from query params
  includeArchived = toSignal(
    this.route.queryParamMap.pipe(map(p => p.get('includeArchived') === 'true')),
    { initialValue: this.route.snapshot.queryParamMap.get('includeArchived') === 'true' },
  );

  effectiveSortBy = computed(() => this.sortBy() || 'name');

  effectiveSortDir = computed(() => {
    const explicit = this.sortDir();
    if (explicit) return explicit;
    const field = this.sortFields.find(f => f.value === this.effectiveSortBy());
    return field?.defaultDesc ? 'desc' : 'asc';
  });

  sortLabel = computed(() =>
    this.sortFields.find(f => f.value === this.effectiveSortBy())?.label ?? 'Sort');

  // Empty states key on the search term, not the archived toggle: an empty result with the toggle on
  // (but no search) is "No payees yet" (the toolbar/toggle stay reachable), never "No matching — reset
  // search", which would offer a reset for a search that isn't there.
  private hasSearch = computed(() => this.searchTerm().length > 0);
  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);
  showInitialEmpty = computed(() => !this.hasItems() && !this.hasSearch() && !this.loading());
  showFilteredEmpty = computed(() => !this.hasItems() && this.hasSearch());

  constructor() {
    // Restore saved list params on a bare navigation (mirrors the Brands list).
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const hasParams = Object.keys(params).length > 0;
      if (!hasParams && this.listState.hasParams) {
        this.ready.set(false);
        this.router.navigate(['/finances/payees'], {
          queryParams: this.listState.getSavedParams(),
          replaceUrl: true,
        });
        this.router.events.pipe(
          filter(e => e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError),
          take(1),
          takeUntilDestroyed(this.destroyRef),
        ).subscribe(() => {
          this.searchInput.set(this.route.snapshot.queryParamMap.get('search') ?? '');
          this.ready.set(true);
        });
      } else if (hasParams) {
        this.listState.saveParams(params);
      }
    });

    toObservable(this.searchInput).pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(value => (value || '') !== (this.route.snapshot.queryParamMap.get('search') ?? '')),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(value => {
      this.router.navigate(['/finances/payees'], {
        queryParams: { page: 1, search: value || null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? this.prefs.pageSize()))),
    { initialValue: this.prefs.pageSize() },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
      toObservable(this.sortBy),
      toObservable(this.sortDir),
      toObservable(this.searchTerm),
      toObservable(this.includeArchived),
      toObservable(this.ready),
    ]).pipe(
      filter(([, , , , , , , ready]) => ready),
      switchMap(([page, pageSize, , , , search, includeArchived]) => {
        this.loading.set(true);
        this.error.set(false);
        // Send the *effective* sort (what the toolbar arrow and table header display), not the raw params.
        return this.payeeApi.getPayees({
          Page: page,
          PageSize: pageSize,
          Search: search || undefined,
          SortBy: this.effectiveSortBy(),
          SortDir: this.effectiveSortDir(),
          IncludeArchived: includeArchived || undefined,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as PaginatedResponseOfPayeeResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.router.navigate(['/finances/payees'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.router.navigate(['/finances/payees'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onIncludeArchivedChange(checked: boolean): void {
    this.router.navigate(['/finances/payees'], {
      queryParams: { page: 1, includeArchived: checked ? true : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortChange(event: PayeeSortChange | null): void {
    this.router.navigate(['/finances/payees'], {
      queryParams: {
        page: 1,
        sortBy: event?.sortBy ?? null,
        sortDir: event?.sortDir ?? null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onSortByChange(field: string): void {
    const defaultDesc = this.sortFields.find(f => f.value === field)?.defaultDesc ?? false;
    this.onSortChange({ sortBy: field, sortDir: this.sortDir() ?? (defaultDesc ? 'desc' : 'asc') });
  }

  onSortDirChange(dir: string): void {
    this.onSortChange({ sortBy: this.effectiveSortBy(), sortDir: dir });
  }

  resetSearch(): void {
    this.searchInput.set('');
    this.router.navigate(['/finances/payees'], {
      queryParams: { page: 1, search: null },
      queryParamsHandling: 'merge',
    });
  }

  // ----- Edit -----

  onEdit(payee: PayeeResponse): void {
    this.selectedPayee.set(payee);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onRefresh();
  }

  onEditClosed(): void {
    this.editState.set('closed');
    this.selectedPayee.set(null);
  }

  // ----- Delete -----

  onDeleteRequest(payee: PayeeResponse): void {
    this.deleteError.set('');
    this.pendingDelete.set(payee);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const payee = this.pendingDelete();
    if (!payee) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.payeeApi.deletePayee({ id: payee.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        this.pendingDelete.set(null);
        this.onRefresh();
        toast.success('Payee deleted.');
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection reason
      // ("in use by transactions — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete payee.'));
      },
    });
  }

  // ----- Merge -----

  onMergeRequest(payee: PayeeResponse): void {
    this.mergeSource.set(payee);
    this.mergeTarget.set(null);
    this.mergeState.set('open');
  }

  onMergeStateChanged(state: BrnDialogState): void {
    this.mergeState.set(state);
    if (state === 'closed') {
      this.mergeSource.set(null);
      this.mergeTarget.set(null);
    }
  }

  confirmMerge(): void {
    const source = this.mergeSource();
    const target = this.mergeTarget();
    if (!source || !target || source.id === target.id) return;

    this.merging.set(true);
    this.payeeApi.mergePayee({ id: source.id, body: { targetId: target.id } }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.merging.set(false);
        this.mergeState.set('closed');
        this.mergeSource.set(null);
        this.mergeTarget.set(null);
        this.onRefresh();
        toast.success('Payees merged.');
      },
      error: (err: unknown) => {
        this.merging.set(false);
        this.errorHandler.handleError(err, 'Failed to merge payees.');
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
