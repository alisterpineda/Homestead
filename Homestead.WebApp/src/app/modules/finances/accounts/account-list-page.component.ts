import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideWallet,
  lucideCircleAlert,
  lucideTriangleAlert,
  lucideListFilter,
  lucideRows3,
  lucideTable,
  lucideEllipsisVertical,
  lucideArrowUp,
  lucideArrowDown,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { injectIsNarrowViewport } from '@/shared/utils/viewport';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { AccountTableComponent, type AccountSortId, type AccountSortState } from './account-table.component';
import { AccountCardGridComponent } from './account-card-grid.component';
import { CreateAccountDialogComponent } from './create-account-dialog/create-account-dialog.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { accountKindLabel } from '../shared/account-kind-meta';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface AccountSortFieldOption {
  readonly id: AccountSortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

function compareNullable<T>(av: T | null, bv: T | null, dir: 1 | -1, lt: (a: T, b: T) => number): number {
  if (av === bv) return 0;
  if (av === null) return 1;
  if (bv === null) return -1;
  return lt(av, bv) * dir;
}

@Component({
  selector: 'app-account-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmButtonGroup,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmDropdownMenuImports,
    AccountTableComponent,
    AccountCardGridComponent,
    CreateAccountDialogComponent,
    EditAccountSheetComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucideWallet,
    lucideCircleAlert,
    lucideTriangleAlert,
    lucideListFilter,
    lucideRows3,
    lucideTable,
    lucideEllipsisVertical,
    lucideArrowUp,
    lucideArrowDown,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
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
            <div hlmEmptyTitle>Failed to load accounts</div>
            <div hlmEmptyDescription>There was an error loading your accounts. Please try again.</div>
          </div>
        </div>
      }

      @if (data() !== null) {
        @if (!settingsService.hasDisplayCurrency() && settingsService.loaded()) {
          <div class="flex items-start gap-3 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
            <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
            <div class="flex-1">
              <p class="font-medium">Display currency not set</p>
              <p class="text-amber-900/80 dark:text-amber-100/80">
                Pick a display currency on the Settings page so balances render correctly.
              </p>
            </div>
            <a hlmBtn variant="outline" routerLink="/finances/settings">Open settings</a>
          </div>
        }

        @if (userAccounts().length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideWallet" />
              </div>
              <div hlmEmptyTitle>No accounts yet</div>
              <div hlmEmptyDescription>Create your first account to start tracking net worth.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-account-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New account
              </app-create-account-dialog>
            </div>
          </div>
        } @else {
          <div class="flex items-center justify-between gap-2">
            <div class="flex items-center gap-2">
              @if (archivedCount() > 0) {
                <div hlmPopover>
                  <button hlmPopoverTrigger hlmBtn variant="outline">
                    <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
                    {{ showArchived() ? 'Filter: Archived' : 'Filter' }}
                  </button>
                  <div hlmPopoverContent *hlmPopoverPortal class="w-[240px] p-0" align="start">
                    <div class="flex items-center justify-between px-3 py-2">
                      <span class="text-sm font-medium">Filters</span>
                      @if (showArchived()) {
                        <button
                          hlmBtn
                          variant="ghost"
                          size="sm"
                          (click)="showArchived.set(false)"
                        >Reset</button>
                      }
                    </div>
                    <span hlmSeparator></span>
                    <div class="p-2">
                      <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                        <hlm-checkbox
                          [checked]="showArchived()"
                          (checkedChange)="showArchived.set($event === true)"
                          [aria-label]="'Show archived accounts'"
                        />
                        <span>Show archived accounts ({{ archivedCount() }})</span>
                      </label>
                    </div>
                  </div>
                </div>
              }

              <button hlmBtn variant="outline" class="hidden sm:inline-flex" [hlmDropdownMenuTrigger]="sortMenu">
                <ng-icon hlm [name]="sortState().desc ? 'lucideArrowDown' : 'lucideArrowUp'" size="sm" />
                {{ sortFieldLabel() }}
              </button>
              <ng-template #sortGroup>
                <span hlmDropdownMenuLabel>Sort by</span>
                <hlm-dropdown-menu-group>
                  @for (opt of sortFields; track opt.id) {
                    <button
                      hlmDropdownMenuRadio
                      [checked]="sortState().id === opt.id"
                      (triggered)="onSortByChange(opt.id)"
                    >
                      <hlm-dropdown-menu-radio-indicator />
                      {{ opt.label }}
                    </button>
                  }
                </hlm-dropdown-menu-group>
                <hlm-dropdown-menu-separator />
                <span hlmDropdownMenuLabel>Direction</span>
                <hlm-dropdown-menu-group>
                  <button
                    hlmDropdownMenuRadio
                    [checked]="!sortState().desc"
                    (triggered)="onSortDirChange(false)"
                  >
                    <hlm-dropdown-menu-radio-indicator />
                    Ascending
                  </button>
                  <button
                    hlmDropdownMenuRadio
                    [checked]="sortState().desc"
                    (triggered)="onSortDirChange(true)"
                  >
                    <hlm-dropdown-menu-radio-indicator />
                    Descending
                  </button>
                </hlm-dropdown-menu-group>
              </ng-template>
              <ng-template #sortMenu>
                <hlm-dropdown-menu class="w-44">
                  <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
                </hlm-dropdown-menu>
              </ng-template>

              <div hlmButtonGroup class="hidden sm:flex" aria-label="View mode">
                <button
                  hlmBtn
                  [variant]="viewMode() === 'card' ? 'secondary' : 'outline'"
                  size="icon"
                  (click)="setViewMode('card')"
                  aria-label="Card view"
                >
                  <ng-icon hlm name="lucideRows3" size="sm" />
                </button>
                <button
                  hlmBtn
                  [variant]="viewMode() === 'table' ? 'secondary' : 'outline'"
                  size="icon"
                  (click)="setViewMode('table')"
                  aria-label="Table view"
                >
                  <ng-icon hlm name="lucideTable" size="sm" />
                </button>
              </div>

              <button hlmBtn variant="outline" size="icon" class="sm:hidden"
                      [hlmDropdownMenuTrigger]="overflowMenu" aria-label="More options">
                <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
              </button>
              <ng-template #overflowMenu>
                <hlm-dropdown-menu class="w-44">
                  <span hlmDropdownMenuLabel>View</span>
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuRadio [checked]="viewMode() === 'card'"
                            (triggered)="setViewMode('card')">
                      <hlm-dropdown-menu-radio-indicator />
                      Card
                    </button>
                    <button hlmDropdownMenuRadio [checked]="viewMode() === 'table'"
                            (triggered)="setViewMode('table')">
                      <hlm-dropdown-menu-radio-indicator />
                      Table
                    </button>
                  </hlm-dropdown-menu-group>
                  <hlm-dropdown-menu-separator />
                  <button hlmDropdownMenuItem [hlmDropdownMenuTrigger]="sortSubMenu">
                    Sort
                    <hlm-dropdown-menu-item-sub-indicator />
                  </button>
                  <ng-template #sortSubMenu>
                    <hlm-dropdown-menu-sub>
                      <ng-container [ngTemplateOutlet]="sortGroup" [ngTemplateOutletInjector]="'outlet'" />
                    </hlm-dropdown-menu-sub>
                  </ng-template>
                </hlm-dropdown-menu>
              </ng-template>
            </div>
            <app-create-account-dialog (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New account
            </app-create-account-dialog>
          </div>

          @if (viewMode() === 'table') {
            <app-account-table
              [accounts]="sortedVisibleAccounts()"
              [displayCurrency]="settingsService.displayCurrency()"
              [sortState]="sortState()"
              (sortChange)="sortState.set($event)"
              (edit)="onEdit($event)"
              (archiveToggle)="onArchiveToggle($event)"
              (delete)="onDeleteRequest($event)"
            />
          } @else {
            <app-account-card-grid
              [accounts]="sortedVisibleAccounts()"
              [displayCurrency]="settingsService.displayCurrency()"
              (edit)="onEdit($event)"
              (archiveToggle)="onArchiveToggle($event)"
              (delete)="onDeleteRequest($event)"
            />
          }
        }
      }
    </div>

    <app-edit-account-sheet
      [account]="selectedAccount()"
      [state]="sheetState()"
      (saved)="onSheetSaved($event)"
      (closed)="onSheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'account' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the account. Accounts with transactions cannot be deleted —
            archive them instead.
          </p>
        </div>
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
export class AccountListPageComponent {
  private accountApi = inject(FinancesAccountApiService);
  protected settingsService = inject(FinanceSettingsService);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);
  private readonly narrow = injectIsNarrowViewport();

  readonly sortFields: readonly AccountSortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'kind', label: 'Type', defaultDesc: false },
    { id: 'latestTransactionDate', label: 'Latest transaction', defaultDesc: true },
    { id: 'displayValue', label: 'Total balance', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  showArchived = signal(false);

  sortState = signal<AccountSortState>({ id: 'name', desc: false });

  viewMode = computed<'table' | 'card'>(() => {
    const stored = this.prefs.accountViewMode();
    if (stored === 'table' || stored === 'card') return stored;
    return this.narrow() ? 'card' : 'table';
  });

  selectedAccount = signal<AccountResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<AccountResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  archiving = signal(false);

  data = toSignal<AccountResponse[] | null, AccountResponse[] | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return this.accountApi.getAccounts({
          Page: 1,
          PageSize: 200,
          IncludeArchived: true,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as AccountResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  userAccounts = computed(() => this.data() ?? []);
  archivedCount = computed(() => this.userAccounts().filter(a => a.isArchived).length);
  visibleAccounts = computed(() => {
    const all = this.userAccounts();
    return this.showArchived() ? all : all.filter(a => !a.isArchived);
  });

  sortedVisibleAccounts = computed(() => {
    const accounts = [...this.visibleAccounts()];
    const { id, desc } = this.sortState();
    const dir: 1 | -1 = desc ? -1 : 1;
    accounts.sort((a, b) => {
      switch (id) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'kind':
          return accountKindLabel(a.kind).localeCompare(accountKindLabel(b.kind)) * dir;
        case 'latestTransactionDate':
          return compareNullable(a.latestTransactionDate ?? null, b.latestTransactionDate ?? null, dir, (x, y) => (x < y ? -1 : 1));
        case 'displayValue':
          return compareNullable(a.displayValue ?? null, b.displayValue ?? null, dir, (x, y) => x - y);
        default:
          return 0;
      }
    });
    return accounts;
  });

  sortFieldLabel = computed(() => {
    const { id } = this.sortState();
    return this.sortFields.find(f => f.id === id)?.label ?? 'Sort';
  });

  setViewMode(mode: 'table' | 'card'): void {
    this.prefs.accountViewMode.set(mode);
  }

  onSortByChange(id: AccountSortId): void {
    const field = this.sortFields.find(f => f.id === id);
    const desc = field?.defaultDesc ?? false;
    this.sortState.set({ id, desc });
  }

  onSortDirChange(desc: boolean): void {
    this.sortState.update(prev => ({ id: prev.id, desc }));
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onEdit(account: AccountResponse): void {
    this.selectedAccount.set(account);
    this.sheetState.set('open');
  }

  onSheetSaved(_: AccountResponse): void {
    this.sheetState.set('closed');
    this.onChanged();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedAccount.set(null);
  }

  onArchiveToggle(account: AccountResponse): void {
    this.archiving.set(true);
    this.accountApi.patchAccount({
      id: account.id,
      body: [replace('/isArchived', !account.isArchived)],
    }).pipe(
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update account.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(account.isArchived ? 'Account unarchived.' : 'Account archived.');
      this.onChanged();
    });
  }

  onDeleteRequest(account: AccountResponse): void {
    this.pendingDelete.set(account);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const account = this.pendingDelete();
    if (!account) return;
    this.deleting.set(true);
    const result$ = this.accountApi.deleteAccount({ id: account.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete account.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Account deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
