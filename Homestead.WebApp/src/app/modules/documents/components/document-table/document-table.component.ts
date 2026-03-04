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
  getSortedRowModel,
} from '@tanstack/angular-table';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { DocumentSortBy } from '@/api/generated/models/document-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';
import { SortHeaderButton } from './sort-header-button';
import { TableHeadSelection, TableRowSelection } from './selection-column';
import { TagsCell, PartiesCell, FileSizeCell } from './table-cells';

const COLUMN_ID_TO_SORT_KEY: Record<string, DocumentSortBy> = {
  name: 'Name',
  contentType: 'ContentType',
  fileSize: 'FileSize',
};

const SORT_KEY_TO_COLUMN_ID: Record<string, string> = {
  Name: 'name',
  ContentType: 'contentType',
  FileSize: 'fileSize',
};

export interface HideableColumn {
  id: string;
  label: string;
}

export const HIDEABLE_COLUMNS: HideableColumn[] = [
  { id: 'parties', label: 'Parties' },
  { id: 'tags', label: 'Tags' },
  { id: 'contentType', label: 'Content Type' },
  { id: 'fileSize', label: 'File Size' },
];

@Component({
  selector: 'app-document-table',
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
                (click)="documentSelect.emit(row.original.id)"
                (keydown.enter)="documentSelect.emit(row.original.id)"
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
export class DocumentTableComponent {
  documents = input.required<GetDocumentsResponse[]>();
  sortBy = input<DocumentSortBy | null | undefined>(null);
  sortDir = input<SortDirection | null | undefined>(null);
  columnVisibility = input<VisibilityState>({});
  selectedIds = input<Set<string>>();

  documentSelect = output<string>();
  sortChange = output<{ sortBy: DocumentSortBy; sortDir: SortDirection } | null>();
  selectionChange = output<Set<string>>();

  private readonly _sorting = signal<SortingState>([]);
  private readonly _rowSelection = signal<RowSelectionState>({});
  private readonly _columnVisibility = signal<VisibilityState>({});

  private readonly _columns: ColumnDef<GetDocumentsResponse>[] = [
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
      cell: (info) => `<span class="font-medium">${escapeHtml(info.getValue<string>())}</span>`,
      enableHiding: false,
      size: 300,
      minSize: 120,
    },
    {
      id: 'parties',
      header: 'Parties',
      enableSorting: false,
      cell: () => flexRenderComponent(PartiesCell),
      size: 200,
      minSize: 100,
    },
    {
      id: 'tags',
      header: 'Tags',
      enableSorting: false,
      cell: () => flexRenderComponent(TagsCell),
      size: 200,
      minSize: 100,
    },
    {
      accessorKey: 'contentType',
      id: 'contentType',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Content Type' } }),
      cell: (info) => `<span class="text-muted-foreground">${escapeHtml(info.getValue<string>())}</span>`,
      size: 150,
      minSize: 80,
    },
    {
      accessorKey: 'fileSize',
      id: 'fileSize',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'File Size' } }),
      cell: () => flexRenderComponent(FileSizeCell),
      size: 100,
      minSize: 60,
    },
  ];

  protected readonly table = createAngularTable<GetDocumentsResponse>(() => ({
    data: this.documents(),
    columns: this._columns,
    getRowId: (row) => row.id,
    manualSorting: true,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
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

  /** CSS variable map for column sizes — bound on the container so the CSS engine handles propagation. */
  protected readonly columnSizeVars = computed(() => {
    const sizing = this.table.getState().columnSizing;
    const sizingInfo = this.table.getState().columnSizingInfo;
    // Read both to establish signal dependencies
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
    parties: 'hidden xl:table-cell',
    contentType: 'hidden lg:table-cell',
    fileSize: 'text-right',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    select: '',
    parties: 'hidden xl:table-cell overflow-hidden',
    contentType: 'hidden lg:table-cell overflow-hidden',
    fileSize: 'text-right overflow-hidden',
    name: 'overflow-hidden',
    tags: 'overflow-hidden',
  };

  getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  constructor() {
    // Sync sorting input from parent (URL query params) → TanStack state
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

    // Sync column visibility input from parent → TanStack state
    effect(() => {
      this._columnVisibility.set(this.columnVisibility());
    });

    // Sync selected IDs input from parent → TanStack row selection state
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

function escapeHtml(str: string): string {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
