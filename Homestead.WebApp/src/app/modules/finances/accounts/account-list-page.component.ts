import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideWallet, lucideCircleAlert, lucideTriangleAlert, lucideListFilter } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { AccountTableComponent } from './account-table.component';
import { CreateAccountDialogComponent } from './create-account-dialog/create-account-dialog.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-account-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    AccountTableComponent,
    CreateAccountDialogComponent,
    EditAccountSheetComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideWallet, lucideCircleAlert, lucideTriangleAlert, lucideListFilter })],
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
            <a hlmBtn variant="outline" size="sm" routerLink="/finances/settings">Open settings</a>
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
                  <button hlmPopoverTrigger hlmBtn variant="outline" size="sm">
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
                          class="h-auto px-2 py-0.5 text-xs"
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
            </div>
            <app-create-account-dialog triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New account
            </app-create-account-dialog>
          </div>

          <app-account-table
            [accounts]="visibleAccounts()"
            [displayCurrency]="settingsService.displayCurrency()"
            (edit)="onEdit($event)"
            (archiveToggle)="onArchiveToggle($event)"
            (delete)="onDeleteRequest($event)"
          />
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

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  showArchived = signal(false);

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

  userAccounts = computed(() => (this.data() ?? []).filter(a => !a.isSystem));
  archivedCount = computed(() => this.userAccounts().filter(a => a.isArchived).length);
  visibleAccounts = computed(() => {
    const all = this.userAccounts();
    return this.showArchived() ? all : all.filter(a => !a.isArchived);
  });

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
