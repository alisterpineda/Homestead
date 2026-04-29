import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
  output,
  signal,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  type SortingState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
  getSortedRowModel,
  injectFlexRenderContext,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { AccountRowActionsComponent } from './account-row-actions.component';
import { KIND_LABELS, isUserAccountKind } from '../shared/account-kind-meta';
import { formatAccountBalance } from '../shared/sign';
import { escapeHtml } from '@/shared/utils';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

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
  private readonly ctx = injectFlexRenderContext<CellContext<AccountResponse, unknown>>();
  protected get account(): AccountResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-account-latest-tx-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class AccountLatestTxCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<AccountResponse, unknown>>();
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
  private readonly ctx = injectFlexRenderContext<CellContext<AccountResponse, unknown>>();
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
                @for (cell of row.getVisibleCells(); track cell.id) {
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
  accounts = input.required<AccountResponse[]>();
  displayCurrency = input<CommodityResponse | null>(null);

  edit = output<AccountResponse>();
  archiveToggle = output<AccountResponse>();
  delete = output<AccountResponse>();

  private readonly _sorting = signal<SortingState>([{ id: 'name', desc: false }]);

  private readonly _meta: AccountTableMeta = {
    onEdit: (a) => this.edit.emit(a),
    onArchiveToggle: (a) => this.archiveToggle.emit(a),
    onDelete: (a) => this.delete.emit(a),
  };

  private readonly _columns = computed<ColumnDef<AccountResponse>[]>(() => {
    const dc = this.displayCurrency();
    return [
      {
        accessorKey: 'name',
        id: 'name',
        header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
        cell: () => flexRenderComponent(AccountNameCell),
      },
      {
        id: 'kind',
        accessorFn: (row) => isUserAccountKind(row.kind) ? KIND_LABELS[row.kind] : '',
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
          const text = formatAccountBalance(account, dc);
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

  protected readonly table = createAngularTable<AccountResponse>(() => ({
    data: this.accounts(),
    columns: this._columns(),
    getRowId: (row) => row.id,
    enableSorting: true,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    onSortingChange: (updater) => {
      const next = updater instanceof Function ? updater(this._sorting()) : updater;
      this._sorting.set(next);
    },
    state: {
      sorting: this._sorting(),
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
