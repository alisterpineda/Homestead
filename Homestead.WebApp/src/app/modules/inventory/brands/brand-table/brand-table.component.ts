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
  flexRenderComponent,
  FlexRenderDirective,
  injectTable,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../../documents/list/document-table/sort-header-button';
import { BrandNameCell, BrandUpdatedCell, BrandActionsCell, type BrandTableMeta } from './table-cells';
import type { BrandResponse } from '@/api/generated/models/brand-response';

export const brandTableFeatures = tableFeatures({
  rowSortingFeature,
});
export type BrandTableFeatures = typeof brandTableFeatures;

/** Column id ⇆ server sort key. The backend takes name | items | updated. */
const COLUMN_ID_TO_SORT_KEY: Record<string, string> = {
  name: 'name',
  itemCount: 'items',
  updatedAt: 'updated',
};
const SORT_KEY_TO_COLUMN_ID: Record<string, string> = {
  name: 'name',
  items: 'itemCount',
  updated: 'updatedAt',
};

export interface BrandSortChange {
  readonly sortBy: string;
  readonly sortDir: string;
}

@Component({
  selector: 'app-brand-table',
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
                <!-- No columnVisibilityFeature here, so all cells render; switch to row.getVisibleCells() if columns become hideable. -->
                @for (cell of row.getAllCells(); track cell.id) {
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
export class BrandTableComponent {
  brands = input.required<BrandResponse[]>();
  sortBy = input<string | null | undefined>(null);
  sortDir = input<string | null | undefined>(null);

  edit = output<BrandResponse>();
  merge = output<BrandResponse>();
  delete = output<BrandResponse>();
  sortChange = output<BrandSortChange | null>();

  private readonly _sorting = signal<SortingState>([]);

  private readonly _meta: BrandTableMeta = {
    onEdit: (b) => this.edit.emit(b),
    onMerge: (b) => this.merge.emit(b),
    onDelete: (b) => this.delete.emit(b),
  };

  private readonly _columns: ColumnDef<BrandTableFeatures, BrandResponse>[] = [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(BrandNameCell),
    },
    {
      id: 'itemCount',
      accessorFn: (row) => row.itemCount,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Items' } }),
      cell: (info) => `<span class="tabular-nums text-muted-foreground">${info.getValue<number>()}</span>`,
    },
    {
      id: 'updatedAt',
      accessorFn: (row) => row.updatedAt,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Updated' } }),
      cell: () => flexRenderComponent(BrandUpdatedCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(BrandActionsCell),
    },
  ];

  protected readonly table = injectTable(() => ({
    features: brandTableFeatures,
    data: this.brands(),
    columns: this._columns,
    getRowId: (row) => row.id,
    enableSorting: true,
    enableMultiSort: false,
    manualSorting: true,
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
    itemCount: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    itemCount: 'text-right tabular-nums overflow-hidden',
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
