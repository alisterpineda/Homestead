import { Component, ChangeDetectionStrategy, input, output, effect, signal, computed, untracked } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type ColumnDef,
  type SortingState,
  type RowSelectionState,
  type VisibilityState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
} from '@tanstack/angular-table';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';
import type { TagSortBy } from '@/api/generated/models/tag-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import { SortHeaderButton } from '../document-table/sort-header-button';
import { TableHeadSelection, TableRowSelection } from '../document-table/selection-column';
import type { HideableColumn } from '../document-table/document-table.component';
import { TagNameCell, TagDateAddedCell } from './table-cells';
import { escapeHtml } from '@/shared/utils';

const COLUMN_ID_TO_SORT_KEY: Record<string, TagSortBy> = {
  name: 'Name',
  path: 'Path',
  documentCount: 'DocumentCount',
  createdAt: 'CreatedAt',
};

const SORT_KEY_TO_COLUMN_ID: Record<string, string> = Object.fromEntries(
  Object.entries(COLUMN_ID_TO_SORT_KEY).map(([k, v]) => [v, k]),
);

export const HIDEABLE_COLUMNS: HideableColumn[] = [
  { id: 'path', label: 'Path' },
  { id: 'description', label: 'Description' },
  { id: 'createdAt', label: 'Date Added' },
];

export const DEFAULT_COLUMN_VISIBILITY: VisibilityState = {
  path: false,
  createdAt: false,
};

@Component({
  selector: 'app-tag-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, FlexRenderDirective],
  template: `
    <div
      class="overflow-clip rounded-md border"
      [class.select-none]="isResizing()"
      [style]="columnSizeVars()"
    >
      <div hlmTableContainer>
        <table hlmTable style="table-layout: fixed; width: 100%">
          <thead hlmTHead class="sticky top-0 z-10 bg-background">
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th
                    hlmTh
                    class="group/resize relative"
                    [class]="getThClass(header.column.id)"
                    [style.width]="'var(--header-' + header.id + '-size)'"
                    [attr.colSpan]="header.colSpan"
                  >
                    @if (!header.isPlaceholder) {
                      <ng-container
                        *flexRender="header.column.columnDef.header; props: header.getContext(); let headerText"
                      >
                        <div [innerHTML]="headerText"></div>
                      </ng-container>
                    }
                    @if (header.column.getCanResize()) {
                      <div
                        class="absolute top-0 right-0 h-full w-1 cursor-col-resize select-none touch-none
                               opacity-0 group-hover/resize:opacity-100 bg-border"
                        [class.!opacity-100]="header.column.getIsResizing()"
                        [class.!bg-primary]="header.column.getIsResizing()"
                        (mousedown)="header.getResizeHandler()($event)"
                        (touchstart)="header.getResizeHandler()($event)"
                      ></div>
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
                tabindex="0"
                (click)="tagSelect.emit(row.original.id)"
                (keydown.enter)="tagSelect.emit(row.original.id)"
                class="cursor-pointer"
                [attr.data-state]="row.getIsSelected() ? 'selected' : undefined"
              >
                @for (cell of row.getVisibleCells(); track cell.id) {
                  <td
                    hlmTd
                    [class]="getTdClass(cell.column.id)"
                    [style.width]="'var(--col-' + cell.column.id + '-size)'"
                    [attr.data-click-passthrough]="cell.column.id === 'select' ? '' : null"
                    (click)="cell.column.id === 'select' ? $event.stopPropagation() : null"
                    (keydown.enter)="cell.column.id === 'select' ? $event.stopPropagation() : null"
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
export class TagTableComponent {
  tags = input.required<GetTagsResponse[]>();
  sortBy = input<TagSortBy | null | undefined>(null);
  sortDir = input<SortDirection | null | undefined>(null);
  columnVisibility = input<VisibilityState>({});
  selectedIds = input<Set<string>>();

  tagSelect = output<string>();
  sortChange = output<{ sortBy: TagSortBy; sortDir: SortDirection } | null>();
  selectionChange = output<Set<string>>();

  private readonly _sorting = signal<SortingState>([]);
  private readonly _rowSelection = signal<RowSelectionState>({});
  private readonly _columnVisibility = signal<VisibilityState>({});

  private readonly _columns: ColumnDef<GetTagsResponse>[] = [
    {
      id: 'select',
      header: () => flexRenderComponent(TableHeadSelection),
      cell: () => flexRenderComponent(TableRowSelection),
      enableSorting: false,
      enableHiding: false,
      enableResizing: false,
      size: 48,
      minSize: 48,
      maxSize: 48,
    },
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(TagNameCell),
      enableHiding: false,
      size: 400,
      minSize: 150,
    },
    {
      accessorKey: 'path',
      id: 'path',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Path' } }),
      cell: (info) => `<span class="text-muted-foreground">${escapeHtml(info.getValue<string>())}</span>`,
      size: 250,
      minSize: 100,
    },
    {
      accessorKey: 'description',
      id: 'description',
      header: 'Description',
      enableSorting: false,
      cell: (info) => {
        const val = info.getValue<string | null>();
        return `<span class="text-muted-foreground line-clamp-1">${val ? escapeHtml(val) : '\u2014'}</span>`;
      },
      size: 300,
      minSize: 100,
    },
    {
      accessorKey: 'documentCount',
      id: 'documentCount',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Documents' } }),
      cell: (info) => `<span class="text-muted-foreground">${info.getValue<number>()}</span>`,
      size: 120,
      minSize: 60,
    },
    {
      accessorKey: 'createdAt',
      id: 'createdAt',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Date Added' } }),
      cell: () => flexRenderComponent(TagDateAddedCell),
      size: 140,
      minSize: 100,
    },
  ];

  protected readonly table = createAngularTable<GetTagsResponse>(() => ({
    data: this.tags(),
    columns: this._columns,
    getRowId: (row) => row.id,
    manualSorting: true,
    getCoreRowModel: getCoreRowModel(),
    enableRowSelection: true,
    enableColumnResizing: true,
    columnResizeMode: 'onChange',
    columnResizeDirection: 'ltr',
    onSortingChange: (updater) => {
      const newSorting = updater instanceof Function ? updater(this._sorting()) : updater;
      this._sorting.set(newSorting);
      if (newSorting.length > 0) {
        const sort = newSorting[0];
        const sortKey = COLUMN_ID_TO_SORT_KEY[sort.id];
        if (sortKey) {
          this.sortChange.emit({ sortBy: sortKey, sortDir: sort.desc ? 'Desc' : 'Asc' });
        }
      } else {
        this.sortChange.emit(null);
      }
    },
    onRowSelectionChange: (updater) => {
      const newSelection = updater instanceof Function ? updater(this._rowSelection()) : updater;
      this._rowSelection.set(newSelection);
      this.selectionChange.emit(new Set(Object.keys(newSelection).filter(k => newSelection[k])));
    },
    onColumnVisibilityChange: (updater) => {
      const newVis = updater instanceof Function ? updater(this._columnVisibility()) : updater;
      this._columnVisibility.set(newVis);
    },
    state: {
      sorting: this._sorting(),
      rowSelection: this._rowSelection(),
      columnVisibility: this._columnVisibility(),
    },
  }));

  protected readonly columnSizeVars = computed(() => {
    const sizing = this.table.getState().columnSizing;
    const sizingInfo = this.table.getState().columnSizingInfo;
    void sizing;
    void sizingInfo;

    const headers = untracked(() => this.table.getFlatHeaders());
    const vars: Record<string, string> = {};
    for (const header of headers) {
      vars[`--header-${header.id}-size`] = `${header.getSize()}px`;
      vars[`--col-${header.column.id}-size`] = `${header.column.getSize()}px`;
    }
    return vars;
  });

  protected readonly isResizing = computed(
    () => !!this.table.getState().columnSizingInfo.isResizingColumn,
  );

  private readonly TH_CLASSES: Record<string, string> = {
    select: '',
    documentCount: 'text-right',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    select: '',
    name: 'overflow-hidden',
    path: 'overflow-hidden',
    description: 'overflow-hidden',
    documentCount: 'text-right overflow-hidden',
    createdAt: 'overflow-hidden',
  };

  getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  constructor() {
    effect(() => {
      const sortBy = this.sortBy();
      const sortDir = this.sortDir();
      if (!sortBy) {
        this._sorting.set([]);
      } else {
        const colId = SORT_KEY_TO_COLUMN_ID[sortBy];
        if (colId) {
          this._sorting.set([{ id: colId, desc: sortDir === 'Desc' }]);
        }
      }
    });

    effect(() => {
      this._columnVisibility.set(this.columnVisibility());
    });

    effect(() => {
      const ids = this.selectedIds();
      if (ids) {
        const selection: RowSelectionState = {};
        ids.forEach(id => { selection[id] = true; });
        this._rowSelection.set(selection);
      } else {
        this._rowSelection.set({});
      }
    });
  }
}
