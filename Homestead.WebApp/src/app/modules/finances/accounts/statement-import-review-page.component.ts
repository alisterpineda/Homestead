import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  effect,
  untracked,
  DestroyRef,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { DomSanitizer } from '@angular/platform-browser';
import { toSignal, toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, of, catchError, map, combineLatest, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideCircleCheck,
  lucideTriangleAlert,
  lucideCircleHelp,
  lucideFileQuestion,
  lucideX,
} from '@ng-icons/lucide';
import {
  type ColumnDef,
  type RowSelectionState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
} from '@tanstack/angular-table';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { TableHeadSelection, TableRowSelection } from '@/shared/components/table/selection-column';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { parseDate } from '@/shared/utils';
import { PayeePickerComponent } from '../shared/payee-picker/payee-picker.component';
import {
  type LineItemEdit,
  type StatementLineTableMeta,
  LineDateCell,
  LineDescriptionCell,
  LineTypeCell,
  LinePayeeCell,
  LineAmountCell,
} from './statement-import-review-cells';
import { toast } from '@spartan-ng/brain/sonner';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { formatCurrency } from '../shared/format-currency';
import { todayIso } from '../shared/date';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { FlowDirection } from '@/api/generated/models/flow-direction';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

type Checksum =
  | { state: 'balanced' }
  | { state: 'off'; amount: string }
  | { state: 'unavailable' };

@Component({
  selector: 'app-statement-import-review-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmInput,
    HlmLabel,
    HlmIconImports,
    HlmResizableImports,
    HlmTabsImports,
    HlmEmptyImports,
    HlmTableImports,
    HlmToggleGroupImports,
    HlmDatePicker,
    HlmDatePickerInput,
    FlexRenderDirective,
    PayeePickerComponent,
  ],
  providers: [provideIcons({
    lucideCircleAlert, lucideCircleCheck, lucideTriangleAlert, lucideCircleHelp, lucideFileQuestion, lucideX,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (data(); as d) {
        <!-- Gate on staged-data presence (d.header), not d.status: the parse job's "Completed" SignalR
             notification (which resolves the client's waitForJob) is sent BEFORE the processor commits
             Status=Completed, so a fast GET can see Status=InProgress with the result already staged. -->
        @if (d.status === 'Failed' || !d.header) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
              <div hlmEmptyTitle>
                {{ d.status === 'Failed' ? 'Statement parsing failed' : 'Still processing' }}
              </div>
              <div hlmEmptyDescription>{{ d.error ?? 'This import is not ready to review yet.' }}</div>
            </div>
            <div hlmEmptyContent>
              <button hlmBtn variant="outline" (click)="backToAccount()">Back to account</button>
            </div>
          </div>
        } @else {
          <!-- Desktop: two-panel resizable -->
          @if (!isMobile()) {
            <div hlmResizableGroup direction="horizontal" class="min-h-0 flex-1 rounded-md border">
              <div hlmResizablePanel [defaultSize]="60" [minSize]="35" class="overflow-hidden">
                <ng-container [ngTemplateOutlet]="previewPanel" />
              </div>
              <hlm-resizable-handle withHandle />
              <div hlmResizablePanel [defaultSize]="40" [minSize]="30" class="flex flex-col overflow-hidden">
                <div class="min-h-0 flex-1 overflow-y-auto">
                  <ng-container [ngTemplateOutlet]="dataPanel" />
                </div>
              </div>
            </div>
          } @else {
            <!-- Mobile: tabs -->
            <hlm-tabs tab="data" class="min-w-0 flex min-h-0 flex-1 flex-col overflow-hidden">
              <hlm-tabs-list class="w-full" aria-label="Statement review tabs">
                <button hlmTabsTrigger="data" class="flex-1">Data</button>
                <button hlmTabsTrigger="preview" class="flex-1">Statement</button>
              </hlm-tabs-list>
              <div hlmTabsContent="data" class="min-h-0 flex-1 overflow-y-auto">
                <ng-container [ngTemplateOutlet]="dataPanel" />
              </div>
              <div hlmTabsContent="preview" class="min-h-0 flex-1 overflow-hidden rounded-md border">
                <ng-container [ngTemplateOutlet]="previewPanel" />
              </div>
            </hlm-tabs>
          }
        }
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Import not found</div>
            <div hlmEmptyDescription>It may have expired or belong to another user.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" (click)="backToAccount()">Back to account</button>
          </div>
        </div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Couldn't load import</div>
            <div hlmEmptyDescription>Something went wrong. Check your connection and try again.</div>
          </div>
          <div hlmEmptyContent class="flex gap-2">
            <button hlmBtn variant="outline" (click)="retry()">Retry</button>
            <button hlmBtn variant="ghost" (click)="backToAccount()">Back to account</button>
          </div>
        </div>
      } @else {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      }
    </div>

    <!-- Preview panel (document render) -->
    <ng-template #previewPanel>
      @if (isPdf() && safeUrl()) {
        <iframe [src]="safeUrl()" class="h-full w-full" title="Statement preview"></iframe>
      } @else if (isImage() && blobUrl()) {
        <div class="flex h-full w-full items-center justify-center bg-black">
          <img [src]="blobUrl()" alt="Statement preview" class="max-h-full max-w-full" />
        </div>
      } @else if (!blobUrl() && !fileError()) {
        <div class="flex h-full items-center justify-center"><hlm-spinner /></div>
      } @else {
        <div class="flex h-full flex-col items-center justify-center gap-2 bg-muted text-muted-foreground">
          <ng-icon hlm name="lucideFileQuestion" size="xl" />
          <span class="text-sm">Preview not available</span>
        </div>
      }
    </ng-template>

    <!-- Extracted data + commit form -->
    <ng-template #dataPanel>
      @if (data(); as d) {
        <div class="flex flex-col gap-5 p-4">
          <div class="flex flex-col gap-1">
            <h2 class="text-lg font-semibold">Review statement</h2>
            <p class="text-sm text-muted-foreground">
              Confirm the closing balance and review the itemized transactions below before importing.
            </p>
          </div>

          <!-- Extracted header -->
          @if (d.header; as h) {
            <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
              @if (h.institution) {
                <dt class="text-muted-foreground">Institution</dt>
                <dd class="text-right">{{ h.institution }}</dd>
              }
              @if (h.accountNumberMasked) {
                <dt class="text-muted-foreground">Account</dt>
                <dd class="text-right">{{ h.accountNumberMasked }}</dd>
              }
              @if (h.currency) {
                <dt class="text-muted-foreground">Currency</dt>
                <dd class="text-right">{{ h.currency }}</dd>
              }
              @if (h.periodStart || h.periodEnd) {
                <dt class="text-muted-foreground">Period</dt>
                <dd class="text-right">{{ h.periodStart ?? '?' }} → {{ h.periodEnd ?? '?' }}</dd>
              }
            </dl>
          }

          <!-- Mis-binding badge -->
          <div
            class="flex items-center gap-2 rounded-md border px-3 py-2 text-sm"
            [class.border-destructive]="d.accountNumberMatch === 'Mismatch'"
            [class.text-destructive]="d.accountNumberMatch === 'Mismatch'"
          >
            @switch (d.accountNumberMatch) {
              @case ('Match') {
                <ng-icon hlm name="lucideCircleCheck" size="sm" class="text-emerald-600" />
                <span>Statement account matches this account.</span>
              }
              @case ('Mismatch') {
                <ng-icon hlm name="lucideTriangleAlert" size="sm" />
                <span>The statement's account number doesn't match this account.</span>
              }
              @default {
                <ng-icon hlm name="lucideCircleHelp" size="sm" class="text-muted-foreground" />
                <span class="text-muted-foreground">Couldn't verify the statement's account number.</span>
              }
            }
          </div>

          @if (d.textTruncated) {
            <div class="flex items-center gap-2 rounded-md border border-amber-500/50 px-3 py-2 text-sm text-amber-600">
              <ng-icon hlm name="lucideTriangleAlert" size="sm" />
              <span>This statement's text was long and may have been truncated — verify no lines are missing.</span>
            </div>
          }

          @if (d.duplicateAssertionExists) {
            <div class="flex items-center gap-2 rounded-md border border-amber-500/50 px-3 py-2 text-sm text-amber-600">
              <ng-icon hlm name="lucideTriangleAlert" size="sm" />
              <span>A balance assertion already exists for this date.</span>
            </div>
          }

          <!-- Feature A: closing-balance assertion -->
          @if (d.proposedBalanceEntry) {
            <div class="flex flex-col gap-4">
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="import-amount">Closing balance</label>
                <input
                  hlmInput
                  id="import-amount"
                  type="number"
                  step="0.01"
                  min="0"
                  [value]="amount()"
                  (input)="amount.set(asValue($event))"
                />
                @if (d.proposedBalanceEntry.sourceAnchor; as anchor) {
                  <p class="text-xs text-muted-foreground">Read from: “{{ anchor }}”</p>
                }
              </div>

              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="import-date">As-of date</label>
                <hlm-date-picker
                  class="w-full"
                  captionLayout="dropdown"
                  [date]="datePicker.date()"
                  (dateChange)="datePicker.set($event)"
                >
                  <hlm-date-picker-input inputId="import-date" placeholder="Select date" />
                </hlm-date-picker>
              </div>

              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="import-memo">Memo (optional)</label>
                <input
                  hlmInput
                  id="import-memo"
                  maxlength="512"
                  placeholder="e.g. June statement"
                  [value]="memo()"
                  (input)="memo.set(asValue($event))"
                />
              </div>
            </div>
          }

          <!-- Feature B: itemized transactions -->
          @if (lineItems().length > 0) {
            <div class="flex flex-col gap-2">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <h3 class="text-sm font-medium">Transactions ({{ lineItems().length }})</h3>
                <div class="flex items-center gap-2">
                  <hlm-toggle-group
                    type="single"
                    nullable="false"
                    variant="outline"
                    aria-label="Filter transactions"
                    [value]="filterMode()"
                    (valueChange)="onFilterChange($event)"
                  >
                    <button hlmToggleGroupItem value="all">All</button>
                    <button hlmToggleGroupItem value="duplicates">Duplicates</button>
                    <button hlmToggleGroupItem value="unmappable">Needs fix</button>
                  </hlm-toggle-group>
                  <span class="text-xs text-muted-foreground">{{ acceptedLines().length }} selected</span>
                </div>
              </div>

              <!-- Advisory checksum banner -->
              @switch (checksum().state) {
                @case ('balanced') {
                  <div class="flex items-center gap-2 rounded-md border border-emerald-500/40 px-3 py-2 text-sm text-emerald-600">
                    <ng-icon hlm name="lucideCircleCheck" size="sm" />
                    <span>Balanced — the selected lines reconcile to the closing balance.</span>
                  </div>
                }
                @case ('off') {
                  <div class="flex items-center gap-2 rounded-md border border-amber-500/50 px-3 py-2 text-sm text-amber-600">
                    <ng-icon hlm name="lucideTriangleAlert" size="sm" />
                    <span>Off by {{ offAmount() }} — some lines may be missing or need editing.</span>
                  </div>
                }
                @default {
                  <div class="flex items-center gap-2 rounded-md border px-3 py-2 text-sm text-muted-foreground">
                    <ng-icon hlm name="lucideCircleHelp" size="sm" />
                    <span>Checksum unavailable — the statement didn't show both an opening and closing balance.</span>
                  </div>
                }
              }

              <!-- Bulk actions for the current selection -->
              @if (hasSelection()) {
                <div class="flex flex-wrap items-center gap-2 rounded-md border bg-muted/40 px-3 py-2">
                  <span class="text-sm text-muted-foreground">{{ selectionCount() }} selected</span>
                  <button hlmBtn variant="outline" (click)="bulkSetDirection('Expense')">Set Expense</button>
                  <button hlmBtn variant="outline" (click)="bulkSetDirection('Income')">Set Income</button>
                  <app-payee-picker
                    class="w-44"
                    ariaLabel="Assign payee to selected lines"
                    placeholder="Assign payee…"
                    [value]="null"
                    (valueChange)="bulkAssignPayee($event)"
                  />
                  <button hlmBtn variant="ghost" (click)="bulkClearPayee()">Clear payee</button>
                  <button
                    hlmBtn
                    variant="outline"
                    size="icon"
                    class="ml-auto"
                    (click)="clearSelection()"
                    aria-label="Clear selection"
                  >
                    <ng-icon hlm name="lucideX" size="sm" />
                  </button>
                </div>
              }

              <!-- Transactions table -->
              <div class="overflow-clip rounded-md border">
                <div hlmTableContainer>
                  <table hlmTable class="w-full">
                    <thead hlmTHead class="bg-background">
                      @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
                        <tr hlmTr>
                          @for (header of headerGroup.headers; track header.id) {
                            <th hlmTh [class]="getThClass(header.column.id)">
                              @if (!header.isPlaceholder) {
                                <ng-container *flexRender="header.column.columnDef.header; props: header.getContext(); let h">
                                  <span>{{ h }}</span>
                                </ng-container>
                              }
                            </th>
                          }
                        </tr>
                      }
                    </thead>
                    <tbody hlmTBody>
                      @for (row of table.getRowModel().rows; track row.id) {
                        <tr hlmTr [class.opacity-50]="!rowCommittable(row.original)">
                          @for (cell of row.getVisibleCells(); track cell.id) {
                            <td hlmTd [class]="getTdClass(cell.column.id)">
                              <ng-container *flexRender="cell.column.columnDef.cell; props: cell.getContext(); let c">
                                <div [innerHTML]="c"></div>
                              </ng-container>
                            </td>
                          }
                        </tr>
                        @if (row.original.duplicateSuspected) {
                          <tr hlmTr>
                            <td hlmTd></td>
                            <td hlmTd [attr.colspan]="5" class="px-2 pb-2 text-xs text-amber-600">
                              <ng-icon hlm name="lucideTriangleAlert" size="xs" /> Possible duplicate of an existing transaction.
                            </td>
                          </tr>
                        }
                      }
                    </tbody>
                  </table>
                </div>
              </div>
              @if (visibleLineItems().length === 0) {
                <p class="rounded-md border border-dashed p-3 text-center text-sm text-muted-foreground">
                  No transactions match this filter.
                </p>
              }
              <p class="text-xs text-muted-foreground">
                Lines import as uncategorised Expense/Income. Greyed rows are missing an amount or date and can't be selected until fixed.
              </p>
            </div>
          }

          <!-- Nothing to import -->
          @if (!d.proposedBalanceEntry && lineItems().length === 0) {
            <div class="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
              No closing balance or transactions were found in this statement. There's nothing to import.
            </div>
          }

          <!-- Footer actions -->
          <div class="flex items-center justify-end gap-2 pt-2">
            <button hlmBtn variant="ghost" (click)="backToAccount()" [disabled]="committing()">
              Cancel
            </button>
            @if (d.proposedBalanceEntry || lineItems().length > 0) {
              <button hlmBtn (click)="commit()" [disabled]="!canCommit() || committing()">
                {{ committing() ? 'Committing…' : 'Commit import' }}
              </button>
            }
          </div>
        </div>
      }
    </ng-template>
  `,
})
export class StatementImportReviewPageComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private statementApi = inject(FinancesStatementApiService);
  private accountApi = inject(FinancesAccountApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);
  private sanitizer = inject(DomSanitizer);
  private sidebarService = inject(HlmSidebarService);
  private destroyRef = inject(DestroyRef);

  isMobile = this.sidebarService.isMobile;

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  loadError = signal(false);
  committing = signal(false);

  private jobId = toSignal(this.route.paramMap.pipe(map((p) => p.get('jobId'))), { initialValue: null });
  private accountRouteId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  data = toSignal(
    combineLatest([toObservable(this.jobId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([jobId]) => {
        if (!jobId) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        this.loadError.set(false);
        return this.statementApi.get({ jobId }).pipe(
          map((d) => {
            this.loading.set(false);
            return d;
          }),
          catchError((err) =>
            handleDetailLoadError(err, { notFound: this.notFound, loadError: this.loadError }, this.loading)),
        );
      }),
    ),
    { initialValue: null as StatementImportResponse | null },
  );

  // The account (for accountType → sign convention) and its reporting currency (for the checksum's money
  // formatting). Fetched lazily off the loaded import; failures degrade to a plain-number checksum.
  private accountIdForLoad = computed(() => this.data()?.accountId ?? null);
  account = toSignal(
    toObservable(this.accountIdForLoad).pipe(
      switchMap((id) =>
        id ? this.accountApi.getAccount({ id }).pipe(catchError(() => of(null))) : of(null)),
    ),
    { initialValue: null as AccountResponse | null },
  );
  reportingCurrency = toSignal(
    toObservable(this.account).pipe(
      switchMap((a) =>
        a ? this.commodityApi.getCommodity({ id: a.reportingCurrencyId }).pipe(catchError(() => of(null))) : of(null)),
    ),
    { initialValue: null as CommodityResponse | null },
  );

  // ─── Editable commit fields, seeded once per import ───────────────────────
  private seedKey = computed(() => `${this.jobId() ?? ''}|${this.data() ? '1' : '0'}`);

  amount = seededLinkedSignal(this.seedKey, () => {
    const be = this.data()?.proposedBalanceEntry;
    return be ? String(be.assertedAmount) : '';
  });
  date = seededLinkedSignal(this.seedKey, () => {
    const be = this.data()?.proposedBalanceEntry;
    return be?.date ?? todayIso();
  });
  memo = seededLinkedSignal(this.seedKey, () => '');

  protected readonly datePicker = isoDateBinding(this.date);

  // Per-row line-item edit state. `accepted` is NOT held here — acceptance lives in the table's
  // rowSelection map. `dateObj` memoizes the parsed Date so the picker binding keeps a stable reference
  // (a fresh Date every change detection would snap an open calendar back — see LineDateCell).
  lineItems = seededLinkedSignal<string, LineItemEdit[]>(this.seedKey, () =>
    (this.data()?.lineItems ?? []).map((r) => ({
      index: r.index,
      amount: String(r.amount),
      description: r.description ?? '',
      direction: r.direction,
      date: r.date ?? '',
      dateObj: parseDate(r.date) ?? undefined,
      mappable: r.mappable,
      duplicateSuspected: r.duplicateSuspected,
      payee: null,
    })),
  );

  // Selection = acceptance. Seeded (keyed on the same seedKey) from the staged proposal's own accepted
  // flags — mappable-and-accepted rows start checked; un-mappable rows start unchecked and stay
  // un-selectable via enableRowSelection until the reviewer fixes them. Read from data() directly, not
  // from the lineItems signal, so there's no inter-signal seeding order to reason about.
  rowSelection = seededLinkedSignal<string, RowSelectionState>(this.seedKey, () => {
    const selection: RowSelectionState = {};
    for (const r of this.data()?.lineItems ?? []) {
      if (r.mappable && r.accepted) selection[String(r.index)] = true;
    }
    return selection;
  });

  // Client-side triage filter — display only. Selection/edit state stay in the untouched lineItems
  // signal, so a row hidden by the filter but still selected commits.
  filterMode = signal<'all' | 'duplicates' | 'unmappable'>('all');
  visibleLineItems = computed(() => {
    const rows = this.lineItems();
    switch (this.filterMode()) {
      case 'duplicates': return rows.filter((r) => r.duplicateSuspected);
      case 'unmappable': return rows.filter((r) => !r.mappable);
      default: return rows;
    }
  });

  // A row can be committed once it carries a positive amount and a date (direction always has a value).
  rowCommittable = (row: LineItemEdit): boolean => {
    const amt = Number(row.amount);
    return row.amount !== '' && Number.isFinite(amt) && amt > 0 && !!row.date;
  };

  private isSelected(index: number): boolean {
    return this.rowSelection()[String(index)] === true;
  }

  // Test-facing helper — asserts acceptance without coupling to the rowSelection map shape.
  isAccepted(index: number): boolean {
    return this.isSelected(index);
  }

  acceptedLines = computed(() =>
    this.lineItems().filter((r) => this.isSelected(r.index) && this.rowCommittable(r)));

  allSelected = computed(() => {
    const committable = this.lineItems().filter((r) => this.rowCommittable(r));
    return committable.length > 0 && committable.every((r) => this.isSelected(r.index));
  });

  selectionCount = computed(() => this.acceptedLines().length);
  hasSelection = computed(() => this.selectionCount() > 0);

  private readonly _meta: StatementLineTableMeta = {
    patchRow: (index, patch) => this.patchRow(index, patch),
    setDirection: (index, direction) => this.setDirection(index, direction),
    onPayeeChange: (index, payee) => this.onRowPayeeChange(index, payee),
  };

  private readonly _columns: ColumnDef<LineItemEdit>[] = [
    {
      id: 'select',
      header: () => flexRenderComponent(TableHeadSelection),
      cell: () => flexRenderComponent(TableRowSelection),
      enableSorting: false,
    },
    { id: 'date', header: 'Date', cell: () => flexRenderComponent(LineDateCell), enableSorting: false },
    { id: 'description', header: 'Description', cell: () => flexRenderComponent(LineDescriptionCell), enableSorting: false },
    { id: 'type', header: 'Type', cell: () => flexRenderComponent(LineTypeCell), enableSorting: false },
    { id: 'payee', header: 'Payee', cell: () => flexRenderComponent(LinePayeeCell), enableSorting: false },
    { id: 'amount', header: 'Amount', cell: () => flexRenderComponent(LineAmountCell), enableSorting: false },
  ];

  // Statement order is load-bearing (reconciliation / running-balance reading), so sorting stays off —
  // which also sidesteps any edit-vs-reorder hazard. The options factory re-runs on visibleLineItems()/
  // rowSelection() change, so enableRowSelection re-evaluates as edits change committability.
  protected readonly table = createAngularTable<LineItemEdit>(() => ({
    data: this.visibleLineItems(),
    columns: this._columns,
    getRowId: (row) => String(row.index),
    enableSorting: false,
    enableRowSelection: (row) => this.rowCommittable(row.original),
    onRowSelectionChange: (updater) => {
      this.rowSelection.set(updater instanceof Function ? updater(this.rowSelection()) : updater);
    },
    getCoreRowModel: getCoreRowModel(),
    state: { rowSelection: this.rowSelection() },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    select: 'w-9',
    amount: 'text-right',
  };
  private readonly TD_CLASSES: Record<string, string> = {
    select: 'w-9 text-center align-middle',
    amount: 'text-right align-middle',
  };
  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }
  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? 'align-middle';
  }

  patchRow(index: number, patch: Partial<LineItemEdit>): void {
    this.lineItems.update((rows) =>
      rows.map((r) =>
        r.index === index
          ? {
              ...r,
              ...patch,
              // Keep the memoized Date in lockstep, but only touch it when `date` actually changed.
              ...(patch.date !== undefined ? { dateObj: parseDate(patch.date) ?? undefined } : {}),
            }
          : r));
  }

  setDirection(index: number, direction: FlowDirection): void {
    this.patchRow(index, { direction });
  }

  onRowPayeeChange(index: number, p: PayeeResponse | null): void {
    this.patchRow(index, { payee: p ? { id: p.id, name: p.name } : null });
  }

  // Retained selection adapters (used by the row/header checkboxes flow through TanStack; these keep the
  // component's public API stable for callers and tests). Writes go straight to the rowSelection signal.
  setRowAccepted(index: number, accepted: boolean): void {
    this.rowSelection.update((sel) => {
      const next = { ...sel };
      if (accepted) next[String(index)] = true;
      else delete next[String(index)];
      return next;
    });
  }

  toggleAll(checked: boolean): void {
    if (!checked) {
      this.rowSelection.set({});
      return;
    }
    const next: RowSelectionState = {};
    for (const r of this.lineItems()) {
      if (this.rowCommittable(r)) next[String(r.index)] = true;
    }
    this.rowSelection.set(next);
  }

  clearSelection(): void {
    this.rowSelection.set({});
  }

  onFilterChange(value: unknown): void {
    if (value === 'all' || value === 'duplicates' || value === 'unmappable') this.filterMode.set(value);
  }

  // ─── Bulk actions over the current selection (accepted ∩ committable) ──────
  private updateSelected(patch: (r: LineItemEdit) => Partial<LineItemEdit>): void {
    this.lineItems.update((rows) =>
      rows.map((r) =>
        this.isSelected(r.index) && this.rowCommittable(r) ? { ...r, ...patch(r) } : r));
  }

  bulkSetDirection(direction: FlowDirection): void {
    this.updateSelected(() => ({ direction }));
  }

  bulkAssignPayee(p: PayeeResponse | null): void {
    if (!p) return;
    this.updateSelected(() => ({ payee: { id: p.id, name: p.name } }));
  }

  bulkClearPayee(): void {
    this.updateSelected(() => ({ payee: null }));
  }

  // ─── Advisory closing-balance checksum (never blocks commit) ──────────────
  // expected = opening + Σ(sign · amount) over selected lines; sign follows the account-type convention
  // (asset: In +1 / Out −1; liability: Out +1 / In −1). Compared to the printed closing balance.
  checksum = computed<Checksum>(() => {
    const h = this.data()?.header;
    const opening = h?.openingBalance;
    const closing = h?.closingBalance;
    if (opening == null || closing == null) return { state: 'unavailable' };

    const isLiability = this.account()?.accountType === 'Liability';
    let sum = 0;
    for (const row of this.acceptedLines()) {
      const isOut = row.direction === 'Expense';
      const sign = isLiability ? (isOut ? 1 : -1) : (isOut ? -1 : 1);
      sum += sign * Number(row.amount);
    }

    const diff = (opening + sum) - closing;
    if (Math.abs(diff) < 0.005) return { state: 'balanced' };

    const currency = this.reportingCurrency();
    const magnitude = Math.abs(diff);
    return {
      state: 'off',
      amount: currency ? formatCurrency(magnitude, currency) : magnitude.toFixed(2),
    };
  });

  offAmount = computed(() => {
    const ck = this.checksum();
    return ck.state === 'off' ? ck.amount : '';
  });

  // The balance assertion is committable when one was proposed and its amount/date are valid.
  balanceCommittable = computed(() => {
    if (!this.data()?.proposedBalanceEntry) return false;
    if (!this.date()) return false;
    const amt = Number(this.amount());
    return this.amount() !== '' && Number.isFinite(amt) && amt >= 0;
  });

  canCommit = computed(() => this.balanceCommittable() || this.acceptedLines().length > 0);

  // ─── Document preview (blob) ──────────────────────────────────────────────
  blobUrl = signal<string | null>(null);
  fileError = signal(false);
  private contentType = signal('');
  private fileSub?: Subscription;

  safeUrl = computed(() => {
    const url = this.blobUrl();
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });
  isPdf = computed(() => this.contentType() === 'application/pdf');
  isImage = computed(() => this.contentType().startsWith('image/'));

  constructor() {
    effect(() => {
      const d = this.data();
      const docId = d?.documentId;
      const verId = d?.documentVersionId;

      this.fileSub?.unsubscribe();
      if (!docId || !verId) return;
      untracked(() => this.fileError.set(false));

      this.fileSub = this.documentApi.getDocumentFile(docId, verId).pipe(
        catchError(() => {
          this.fileError.set(true);
          return of(null);
        }),
      ).subscribe((blob) => {
        this.contentType.set(blob?.type ?? '');
        this.swapBlobUrl(blob ? URL.createObjectURL(blob) : null);
      });
    });

    this.destroyRef.onDestroy(() => {
      this.fileSub?.unsubscribe();
      const url = this.blobUrl();
      if (url) URL.revokeObjectURL(url);
    });
  }

  private swapBlobUrl(newUrl: string | null): void {
    const old = this.blobUrl();
    this.blobUrl.set(newUrl);
    if (old) URL.revokeObjectURL(old);
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  retry(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  backToAccount(): void {
    const accountId = this.data()?.accountId ?? this.accountRouteId();
    if (accountId) void this.router.navigate(['/finances/accounts', accountId]);
    else void this.router.navigate(['/finances/accounts']);
  }

  commit(): void {
    const jobId = this.jobId();
    if (!jobId || !this.canCommit()) return;

    // Feature A (balance entry) and Feature B (lines) commit together, atomically. The server ignores the
    // amount/date when accepted=false, so we still send valid placeholders.
    const acceptBalance = this.balanceCommittable();

    this.committing.set(true);
    this.statementApi.commit({
      jobId,
      body: {
        accepted: acceptBalance,
        assertedAmount: acceptBalance ? Number(this.amount()) : 0,
        date: this.date() || todayIso(),
        memo: this.memo().trim() ? this.memo().trim() : null,
        lineItems: this.acceptedLines().map((r) => ({
          direction: r.direction,
          amount: Number(r.amount),
          date: r.date,
          description: r.description.trim() ? r.description.trim() : null,
          payeeId: r.payee?.id ?? null,
        })),
      },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.committing, 'Failed to commit import.'),
    ).subscribe({
      next: (result) => {
        this.committing.set(false);
        toast.success('Statement imported.');
        void this.router.navigate(['/finances/accounts', result.accountId]);
      },
    });
  }
}
