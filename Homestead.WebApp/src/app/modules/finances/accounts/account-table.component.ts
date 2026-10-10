import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
  output,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  flexRenderComponent,
  FlexRenderDirective,
  injectFlexRenderContext,
  injectTable,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { AccountRowActionsComponent } from './account-row-actions.component';
import { accountKindLabel } from '../shared/account-kind-meta';
import { formatAccountBalance } from '../shared/sign';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { escapeHtml } from '@/shared/utils';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const accountTableFeatures = tableFeatures({
  rowSortingFeature,
});
type AccountTableFeatures = typeof accountTableFeatures;

export type AccountSortId = 'name' | 'kind' | 'latestTransactionDate' | 'displayValue';
export interface AccountSortState {
  readonly id: AccountSortId;
  readonly desc: boolean;
}

interface AccountTableMeta {
  onEdit: (account: AccountResponse) => void;
  onArchiveToggle: (account: AccountResponse) => void;
  onDelete: (account: AccountResponse) => void;
}

@Component({
  selector: 'app-account-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <a
      [routerLink]="['/finances/accounts', account.id]"
      class="block min-w-0"
      (click)="$event.stopPropagation()"
    >
      <span class="truncate text-sm font-medium">{{ account.name }}</span>
      @if (account.institution) {
        <span class="block truncate text-xs text-muted-foreground">{{ account.institution }}</span>
      }
    </a>
  `,
})
export class AccountNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<AccountTableFeatures, AccountResponse, unknown>>();
  protected get account(): AccountResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-account-latest-tx-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class AccountLatestTxCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<AccountTableFeatures, AccountResponse, unknown>>();
  protected get formatted(): string {
    const value = this.ctx.row.original.latestTransactionDate;
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-account-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountRowActionsComponent],
  template: `
    <app-account-row-actions
      [account]="account"
      (edit)="onEdit($event)"
      (archiveToggle)="onArchiveToggle($event)"
      (delete)="onDelete($event)"
    />
  `,
})
export class AccountActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<AccountTableFeatures, AccountResponse, unknown>>();
  private get meta(): AccountTableMeta { return this.ctx.table.options.meta as AccountTableMeta; }
  protected get account(): AccountResponse { return this.ctx.row.original; }
  protected onEdit(a: AccountResponse): void { this.meta.onEdit(a); }
  protected onArchiveToggle(a: AccountResponse): void { this.meta.onArchiveToggle(a); }
  protected onDelete(a: AccountResponse): void { this.meta.onDelete(a); }
}

@Component({
  selector: 'app-account-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, FlexRenderDirective],
  template: `
    <div class="overflow-clip rounded-md border">
      <div hlmTableContainer>
        <table hlmTable class="w-full">
          <thead hlmTHead class="bg-background">
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th
                    hlmTh
                    [class]="getThClass(header.column.id)"
                    [attr.colSpan]="header.colSpan"
                  >
                    @if (!header.isPlaceholder) {
                      <ng-container
                        *flexRender="header.column.columnDef.header; props: header.getContext(); let headerText"
                      >
                        <div [innerHTML]="headerText"></div>
                      </ng-container>
                    }
                  </th>
                }
              </tr>
            }
          </thead>
          <tbody hlmTBody>
            @for (row of table.getRowModel().rows; track row.id) {
              <tr
                hlmTr
                [class]="getRowClass(row.original)"
              >
                <!-- No columnVisibilityFeature here, so all cells render; switch to row.getVisibleCells() if columns become hideable. -->
                @for (cell of row.getAllCells(); track cell.id) {
                  <td
                    hlmTd
                    [class]="getTdClass(cell.column.id)"
                  >
                    <ng-container *flexRender="cell.column.columnDef.cell; props: cell.getContext(); let cellText">
                      <div [innerHTML]="cellText"></div>
                    </ng-container>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class AccountTableComponent {
  private readonly prefs = inject(UserPreferencesService);

  accounts = input.required<AccountResponse[]>();
  displayCurrency = input<CommodityResponse | null>(null);
  sortState = input<AccountSortState>({ id: 'name', desc: false });

  edit = output<AccountResponse>();
  archiveToggle = output<AccountResponse>();
  delete = output<AccountResponse>();
  sortChange = output<AccountSortState>();

  private readonly _meta: AccountTableMeta = {
    onEdit: (a) => this.edit.emit(a),
    onArchiveToggle: (a) => this.archiveToggle.emit(a),
    onDelete: (a) => this.delete.emit(a),
  };

  private readonly _columns = computed<ColumnDef<AccountTableFeatures, AccountResponse>[]>(() => {
    const dc = this.displayCurrency();
    const masked = this.prefs.financesPrivacyMasked();
    return [
      {
        accessorKey: 'name',
        id: 'name',
        header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
        cell: () => flexRenderComponent(AccountNameCell),
      },
      {
        id: 'kind',
        accessorFn: (row) => accountKindLabel(row.kind),
        header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Type' } }),
        cell: (info) => {
          const label = info.getValue<string>();
          return label
            ? `<span class="text-muted-foreground">${escapeHtml(label)}</span>`
            : '<span class="text-muted-foreground">—</span>';
        },
      },
      {
        id: 'latestTransactionDate',
        accessorFn: (row) => row.latestTransactionDate ?? undefined,
        sortUndefined: 'last',
        header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Latest transaction' } }),
        cell: () => flexRenderComponent(AccountLatestTxCell),
      },
      {
        id: 'displayValue',
        accessorFn: (row) => row.displayValue ?? undefined,
        sortUndefined: 'last',
        header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Total balance' } }),
        cell: (info) => {
          const account = info.row.original;
          const text = formatAccountBalance(account, dc, masked);
          if (text === null && (account.unpricedHoldingCount ?? 0) > 0) {
            return '<span class="text-xs text-amber-600 dark:text-amber-500">Unpriced</span>';
          }
          return `<span class="tabular-nums">${escapeHtml(text ?? '—')}</span>`;
        },
      },
      {
        id: 'actions',
        header: '',
        enableSorting: false,
        cell: () => flexRenderComponent(AccountActionsCell),
      },
    ];
  });

  protected readonly table = injectTable(() => ({
    features: accountTableFeatures,
    data: this.accounts(),
    columns: this._columns(),
    getRowId: (row) => row.id,
    enableSorting: true,
    enableMultiSort: false,
    manualSorting: true,
    onSortingChange: (updater) => {
      const current = [this.sortState()];
      const next = updater instanceof Function ? updater(current) : updater;
      const first = next[0] ?? { id: 'name', desc: false };
      const prev = this.sortState();
      if (prev.id === first.id && prev.desc === first.desc) return;
      this.sortChange.emit({ id: first.id as AccountSortId, desc: first.desc });
    },
    state: {
      sorting: [this.sortState()],
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    displayValue: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    kind: 'overflow-hidden',
    latestTransactionDate: 'overflow-hidden',
    displayValue: 'text-right tabular-nums overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  protected getRowClass(account: AccountResponse): string {
    return account.isArchived ? 'italic text-muted-foreground' : '';
  }
}
