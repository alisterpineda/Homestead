import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  viewChild,
  TemplateRef,
  afterNextRender,
  OnDestroy,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Router, NavigationEnd } from '@angular/router';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, filter, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideWallet, lucideCircleAlert, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { AccountTableComponent } from './account-table.component';
import { CreateAccountDialogComponent } from './create-account-dialog/create-account-dialog.component';
import { EditAccountSheetComponent } from './edit-account-sheet/edit-account-sheet.component';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
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
    AccountTableComponent,
    CreateAccountDialogComponent,
    EditAccountSheetComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideWallet, lucideCircleAlert, lucideTriangleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerActions>
      <app-create-account-dialog triggerSize="sm" (created)="onChanged()">
        <ng-icon hlm name="lucidePlus" size="sm" />
        New account
      </app-create-account-dialog>
    </ng-template>

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
          @if (archivedCount() > 0) {
            <label class="flex items-center gap-2" hlmLabel>
              <hlm-checkbox
                [checked]="showArchived()"
                (checkedChange)="showArchived.set($event === true)"
                aria-label="Show archived accounts"
              />
              <span>Show archived ({{ archivedCount() }})</span>
            </label>
          }

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
export class AccountListPageComponent implements OnDestroy {
  private accountApi = inject(FinancesAccountApiService);
  protected settingsService = inject(FinanceSettingsService);
  private errorHandler = inject(ErrorHandlerService);
  private pageHeaderService = inject(PageHeaderService);
  private router = inject(Router);

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

  private headerActionsTpl = viewChild.required<TemplateRef<unknown>>('headerActions');

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

  constructor() {
    afterNextRender(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
      this.pageHeaderService.setActions(this.headerActionsTpl());
    });
  }

  ngOnDestroy(): void {
    this.pageHeaderService.clearActions();
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
