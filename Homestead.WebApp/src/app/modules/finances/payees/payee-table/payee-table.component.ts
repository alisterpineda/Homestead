import {
  Component,
  ChangeDetectionStrategy,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type ColumnDef,
  type SortingState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../../documents/list/document-table/sort-header-button';
import { PayeeNameCell, PayeeUpdatedCell, PayeeActionsCell, type PayeeTableMeta } from './table-cells';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

/** Column id ⇆ server sort key. The backend takes name | transactions | updated. */
const COLUMN_ID_TO_SORT_KEY: Record<string, string> = {
  name: 'name',
  transactionCount: 'transactions',
  updatedAt: 'updated',
};
const SORT_KEY_TO_COLUMN_ID: Record<string, string> = {
  name: 'name',
  transactions: 'transactionCount',
  updated: 'updatedAt',
};

export interface PayeeSortChange {
  readonly sortBy: string;
  readonly sortDir: string;
}

@Component({
  selector: 'app-payee-table',
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
                  <th hlmTh [class]="getThClass(header.column.id)" [attr.colSpan]="header.colSpan">
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
              <tr hlmTr>
                @for (cell of row.getVisibleCells(); track cell.id) {
                  <td hlmTd [class]="getTdClass(cell.column.id)">
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
export class PayeeTableComponent {
  payees = input.required<PayeeResponse[]>();
  sortBy = input<string | null | undefined>(null);
  sortDir = input<string | null | undefined>(null);

  edit = output<PayeeResponse>();
  merge = output<PayeeResponse>();
  delete = output<PayeeResponse>();
  sortChange = output<PayeeSortChange | null>();

  private readonly _sorting = signal<SortingState>([]);

  private readonly _meta: PayeeTableMeta = {
    onEdit: (p) => this.edit.emit(p),
    onMerge: (p) => this.merge.emit(p),
    onDelete: (p) => this.delete.emit(p),
  };

  private readonly _columns: ColumnDef<PayeeResponse>[] = [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(PayeeNameCell),
    },
    {
      id: 'defaultCategory',
      accessorFn: (row) => row.defaultCategoryName,
      // Unsortable — the default category is an optional label, not a stable sort axis.
      enableSorting: false,
      header: 'Default category',
      cell: (info) =>
        `<span class="text-muted-foreground">${info.getValue<string | null>() ?? '—'}</span>`,
    },
    {
      id: 'transactionCount',
      accessorFn: (row) => row.transactionCount,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Transactions' } }),
      cell: (info) => `<span class="tabular-nums text-muted-foreground">${info.getValue<number>()}</span>`,
    },
    {
      id: 'updatedAt',
      accessorFn: (row) => row.updatedAt,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Updated' } }),
      cell: () => flexRenderComponent(PayeeUpdatedCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(PayeeActionsCell),
    },
  ];

  protected readonly table = createAngularTable<PayeeResponse>(() => ({
    data: this.payees(),
    columns: this._columns,
    getRowId: (row) => row.id,
    enableSorting: true,
    enableMultiSort: false,
    manualSorting: true,
    getCoreRowModel: getCoreRowModel(),
    onSortingChange: (updater) => {
      const next = updater instanceof Function ? updater(this._sorting()) : updater;
      this._sorting.set(next);
      if (next.length > 0) {
        const sortKey = COLUMN_ID_TO_SORT_KEY[next[0].id];
        if (sortKey) {
          this.sortChange.emit({ sortBy: sortKey, sortDir: next[0].desc ? 'desc' : 'asc' });
        }
      } else {
        this.sortChange.emit(null);
      }
    },
    state: {
      sorting: this._sorting(),
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    transactionCount: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    defaultCategory: 'overflow-hidden',
    transactionCount: 'text-right tabular-nums overflow-hidden',
    updatedAt: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  constructor() {
    // Mirror the URL-driven sort state into the table so the header arrows reflect ?sortBy/?sortDir.
    effect(() => {
      const sortBy = this.sortBy();
      const sortDir = this.sortDir();
      if (!sortBy) {
        this._sorting.set([]);
        return;
      }
      const colId = SORT_KEY_TO_COLUMN_ID[sortBy];
      if (colId) {
        this._sorting.set([{ id: colId, desc: sortDir === 'desc' }]);
      }
    });
  }
}
