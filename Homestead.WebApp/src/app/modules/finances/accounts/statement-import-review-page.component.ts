import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  effect,
  untracked,
  linkedSignal,
  DestroyRef,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { DomSanitizer } from '@angular/platform-browser';
import { toSignal, toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, of, catchError, map, combineLatest, forkJoin, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleAlert,
  lucideCircleCheck,
  lucideTriangleAlert,
  lucideCircleHelp,
  lucideFileQuestion,
  lucideSparkles,
  lucideX,
  lucidePlus,
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
import { HlmButtonGroupImports } from '@/shared/components/spartan/button-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmToggleGroupImports } from '@/shared/components/spartan/toggle-group';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { TableHeadSelection, TableRowSelection } from '@/shared/components/table/selection-column';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { parseDate } from '@/shared/utils';
import { PayeePickerComponent } from '../shared/payee-picker/payee-picker.component';
import {
  type LineItemEdit,
  type LineItemOriginal,
  type LineCategory,
  type StatementLineTableMeta,
  LineDateCell,
  LineDescriptionCell,
  LineTypeCell,
  LinePayeeCell,
  LineAmountCell,
  LineActionsCell,
} from './statement-import-review-cells';
import { toast } from '@spartan-ng/brain/sonner';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { FinancesSettingsApiService } from '@/api/services/finances-settings-api.service';
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
import type { AccountType } from '@/api/generated/models/account-type';
import type { StatementLineItemResponse } from '@/api/generated/models/statement-line-item-response';

type Checksum =
  | { state: 'balanced' }
  | { state: 'off'; amount: string }
  | { state: 'unavailable' };

// The suggested existing-payee's default category as a chip value — only when auto-cat is on and the payee
// carries a category. A tier-③ new payee has none.
function suggestedCategoryOf(r: StatementLineItemResponse, autoCategoriseEnabled: boolean): LineCategory | null {
  if (!autoCategoriseEnabled) return null;
  if (!r.suggestedPayeeDefaultCategoryName || !r.suggestedPayeeDefaultCategoryType) return null;
  return { name: r.suggestedPayeeDefaultCategoryName, type: r.suggestedPayeeDefaultCategoryType };
}

// A picked/created payee's default category as a chip value (gated by the same setting).
function categoryOfPayee(p: PayeeResponse, autoCategoriseEnabled: boolean): LineCategory | null {
  if (!autoCategoriseEnabled) return null;
  if (!p.defaultCategoryName || !p.defaultCategoryType) return null;
  return { name: p.defaultCategoryName, type: p.defaultCategoryType };
}

// Value-equality for the reset-able category chip (used by the isDirty check).
function sameCategory(a: LineCategory | null, b: LineCategory | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return a.name === b.name && a.type === b.type;
}

@Component({
  selector: 'app-statement-import-review-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmButton,
    HlmButtonGroupImports,
    HlmSpinner,
    HlmInput,
    HlmLabel,
    HlmIconImports,
    HlmResizableImports,
    HlmTabsImports,
    HlmEmptyImports,
    HlmTableImports,
    HlmToggleGroupImports,
    HlmAlertDialogImports,
    HlmDatePicker,
    HlmDatePickerInput,
    FlexRenderDirective,
    PayeePickerComponent,
  ],
  providers: [provideIcons({
    lucideCircleAlert, lucideCircleCheck, lucideTriangleAlert, lucideCircleHelp, lucideFileQuestion,
    lucideSparkles, lucideX, lucidePlus,
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
              <div hlmResizablePanel [defaultSize]="40" [minSize]="35" class="overflow-hidden">
                <ng-container [ngTemplateOutlet]="previewPanel" />
              </div>
              <hlm-resizable-handle withHandle />
              <div hlmResizablePanel [defaultSize]="60" [minSize]="30" class="flex flex-col overflow-hidden">
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

          <!-- Feature B: itemized transactions — the import manifest. Shown whenever the import loaded, even
               with zero parsed lines, so a user can build manual rows on a balance-only statement. -->
          @if (d.header) {
            <div class="flex flex-col gap-2">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <h3 class="text-sm font-medium">Transactions ({{ allRows().length }})</h3>
                <div class="flex items-center gap-2">
                  @if (hasSuggestions()) {
                    <div hlmButtonGroup>
                      <button hlmBtn size="sm" variant="outline" (click)="acceptAllSuggestions()">
                        <ng-icon hlm name="lucideSparkles" size="sm" class="mr-1 text-violet-500" />
                        Accept all ({{ suggestionCount() }})
                      </button>
                      <button hlmBtn size="sm" variant="outline" (click)="showSuggestions()">Review</button>
                    </div>
                  }
                  <hlm-toggle-group
                    type="single"
                    nullable="false"
                    variant="outline"
                    aria-label="Filter transactions"
                    [value]="filterMode()"
                    (valueChange)="onFilterChange($event)"
                  >
                    <button hlmToggleGroupItem value="all">All</button>
                    <button hlmToggleGroupItem value="excluded">Excluded</button>
                    <button hlmToggleGroupItem value="duplicates">Duplicates</button>
                    <button hlmToggleGroupItem value="needsfix">Needs fix</button>
                    @if (hasSuggestions()) {
                      <button hlmToggleGroupItem value="suggestions">Suggestions</button>
                    }
                  </hlm-toggle-group>
                  <span class="text-xs text-muted-foreground">{{ linesToImport().length }} importing</span>
                </div>
              </div>

              <!-- Advisory checksum banner -->
              @switch (checksum().state) {
                @case ('balanced') {
                  <div class="flex items-center gap-2 rounded-md border border-emerald-500/40 px-3 py-2 text-sm text-emerald-600">
                    <ng-icon hlm name="lucideCircleCheck" size="sm" />
                    <span>Balanced — the included lines reconcile to the closing balance.</span>
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

              <!-- Bulk actions for the current selection (transient) -->
              @if (hasSelection()) {
                <div class="flex flex-wrap items-center gap-2 rounded-md border bg-muted/40 px-3 py-2">
                  <span class="text-sm text-muted-foreground">{{ selectionCount() }} selected</span>
                  <div hlmButtonGroup>
                    <button hlmBtn variant="outline" (click)="bulkSetDirection('Expense')">Set Expense</button>
                    <button hlmBtn variant="outline" (click)="bulkSetDirection('Income')">Set Income</button>
                  </div>
                  <div hlmButtonGroup>
                    <button hlmBtn variant="outline" (click)="bulkSetExcluded(true)">Exclude</button>
                    <button hlmBtn variant="outline" (click)="bulkSetExcluded(false)">Include</button>
                  </div>
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
              @if (allRows().length > 0) {
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
                          <tr
                            hlmTr
                            [class.opacity-50]="row.original.excluded"
                          >
                            @for (cell of row.getVisibleCells(); track cell.id) {
                              <td hlmTd [class]="getTdClass(cell.column.id)">
                                <ng-container *flexRender="cell.column.columnDef.cell; props: cell.getContext(); let c">
                                  <div [innerHTML]="c"></div>
                                </ng-container>
                              </td>
                            }
                          </tr>
                          @if (needsFix(row.original)) {
                            <tr hlmTr>
                              <td hlmTd></td>
                              <td hlmTd [attr.colspan]="6" class="px-2 pb-2 text-xs text-amber-600">
                                <ng-icon hlm name="lucideTriangleAlert" size="xs" /> Needs an amount and date — fix it or exclude it to import.
                              </td>
                            </tr>
                          }
                          @if (row.original.duplicateSuspected) {
                            <tr hlmTr>
                              <td hlmTd></td>
                              <td hlmTd [attr.colspan]="6" class="px-2 pb-2 text-xs text-amber-600">
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
              } @else {
                <p class="rounded-md border border-dashed p-3 text-center text-sm text-muted-foreground">
                  No transactions were parsed. Add any the statement listed with “+ Add row” below.
                </p>
              }

              <!-- Add a manual row the parser missed -->
              <button
                hlmBtn
                variant="outline"
                size="sm"
                class="w-full border-dashed"
                (click)="addManualRow()"
              >
                <ng-icon hlm name="lucidePlus" size="sm" class="mr-1" />
                Add row
              </button>

              <p class="text-xs text-muted-foreground">
                Pick a payee per line — a learned payee (or one you confirm) is remembered for next time, and
                its default category is applied automatically. Lines without a payee import as uncategorised.
                Every included row imports; use the ⋮ menu to exclude a row (it stays, greyed and struck
                through, and won't import) or reset your edits. Manual rows are never learned.
              </p>
            </div>
          }

          <!-- Footer actions -->
          <div class="flex flex-wrap items-center justify-end gap-2 pt-2">
            @if (needsFixCount() > 0) {
              <p class="mr-auto text-sm text-amber-600">
                {{ needsFixCount() }} row(s) need fixing — fix or exclude them before importing.
              </p>
            }
            <button hlmBtn variant="ghost" (click)="backToAccount()" [disabled]="committing()">
              Cancel
            </button>
            @if (d.proposedBalanceEntry || allRows().length > 0) {
              <button hlmBtn (click)="commit()" [disabled]="!canCommit() || committing()">
                {{ committing() ? 'Committing…' : 'Commit import' }}
              </button>
            }
          </div>
        </div>
      }
    </ng-template>

    <!-- Import-gate warning: un-reviewed AI suggestions would otherwise commit un-attributed -->
    <hlm-alert-dialog [state]="warnState()" (stateChanged)="warnState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal>
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Import {{ pendingGhostCount() }} unreviewed suggestion(s)?</h2>
          <p hlmAlertDialogDescription>
            Some selected lines have an AI-suggested payee you haven't accepted. You can review them first, or
            apply the suggestions and import now — applied-this-way payees are used but not remembered.
          </p>
        </div>
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="reviewSuggestions()">Review them</button>
          <button hlmAlertDialogAction (click)="applyAllAndImport()">Apply all &amp; import</button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class StatementImportReviewPageComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private statementApi = inject(FinancesStatementApiService);
  private accountApi = inject(FinancesAccountApiService);
  private payeeApi = inject(FinancesPayeeApiService);
  private settingsApi = inject(FinancesSettingsApiService);
  private commodityApi = inject(FinancesCommodityApiService);
  private documentApi = inject(DocumentApiService);
  private errorHandler = inject(ErrorHandlerService);
  private sanitizer = inject(DomSanitizer);
  private sidebarService = inject(HlmSidebarService);
  private destroyRef = inject(DestroyRef);

  // Whether the auto-category chip is shown (mirrors the server's AutoCategoriseFromPayee, default on). Only
  // gates the read-only preview chip — the server independently decides what to apply at commit.
  private autoCategoriseEnabled = toSignal(
    this.settingsApi.getFinanceSettings().pipe(
      // Effective value defaults on (the server also treats absent as on).
      map((s) => s.autoCategoriseFromPayee ?? true),
      catchError(() => of(true)),
    ),
    { initialValue: true },
  );

  isMobile = this.sidebarService.isMobile;

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  loadError = signal(false);
  committing = signal(false);
  // Warning dialog shown on Import when un-reviewed AI ghosts would otherwise commit un-attributed.
  warnState = signal<BrnDialogState>('closed');

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
  // The auto-cat flag is folded into the key so a late settings load re-seeds the category chips. It defaults
  // to true (the common case), so a true→true resolution never re-seeds and never clobbers pending edits.
  private seedKey = computed(
    () => `${this.jobId() ?? ''}|${this.data() ? '1' : '0'}|${this.autoCategoriseEnabled() ? '1' : '0'}`);

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

  // Parsed rows — the staged proposal turned into editable manifest rows, re-seeded on seedKey change (a
  // late finance-settings load flips autoCat and re-seeds). Each row starts included (`excluded: false`),
  // non-manual, and carries an immutable `original` snapshot for Reset. `dateObj` memoizes the parsed Date
  // so the picker binding keeps a stable reference (a fresh Date every change detection would snap an open
  // calendar back — see LineDateCell).
  parsedRows = seededLinkedSignal<string, LineItemEdit[]>(this.seedKey, () => {
    const autoCat = this.autoCategoriseEnabled();
    return (this.data()?.lineItems ?? []).map((r) => {
      // Tier-① learned hits are pre-applied (the payee is set, marked "learned", learn=false → apply but
      // don't re-teach). AI ghosts (tier ②/③) stay unapplied (payee=null) until explicitly accepted.
      const isLearned = r.suggestionSource === 'Learned' && !!r.suggestedPayeeId;
      const suggestedCategory = suggestedCategoryOf(r, autoCat);
      const payee = isLearned ? { id: r.suggestedPayeeId!, name: r.suggestedPayeeName ?? '' } : null;
      const original: LineItemOriginal = {
        amount: String(r.amount),
        description: r.description ?? '',
        direction: r.direction,
        date: r.date ?? '',
        payee,
        suggestionSource: r.suggestionSource,
        suggestedPayeeId: r.suggestedPayeeId ?? null,
        suggestedPayeeName: r.suggestedPayeeName ?? null,
        suggestedCategory,
        category: isLearned ? suggestedCategory : null,
        learn: false,
        dismissed: false,
      };
      return {
        index: r.index,
        amount: original.amount,
        description: original.description,
        direction: original.direction,
        date: original.date,
        dateObj: parseDate(r.date) ?? undefined,
        mappable: r.mappable,
        duplicateSuspected: r.duplicateSuspected,
        dismissed: false,
        payee,
        suggestionSource: r.suggestionSource,
        suggestedPayeeId: r.suggestedPayeeId ?? null,
        suggestedPayeeName: r.suggestedPayeeName ?? null,
        suggestedCategory,
        learn: false,
        category: isLearned ? suggestedCategory : null,
        manual: false,
        excluded: false,
        original,
      } satisfies LineItemEdit;
    });
  });

  // Manual rows the user added — kept OUT of parsedRows so the autoCat re-seed (which fires on a late
  // settings load, jobId unchanged) never drops them. Resets to [] only when navigating to a different
  // import (jobId changes). Manual rows carry negative keys from this decrementing counter.
  manualRows = linkedSignal<string | null, LineItemEdit[]>({
    source: this.jobId,
    computation: () => [],
  });
  private nextManualKey = -1;

  // The single source every downstream selector, the table data, bulk ops, checksum, and the ghost-gate
  // read. Parsed rows first (statement order), then manual rows appended.
  allRows = computed(() => [...this.parsedRows(), ...this.manualRows()]);

  // Selection is now PURELY transient bulk-targeting — no longer seeded from the staged `accepted` flag,
  // decoupled from both the include/exclude axis and committability. Resets to empty on import change.
  rowSelection = linkedSignal<string | null, RowSelectionState>({
    source: this.jobId,
    computation: () => ({}),
  });

  // Client-side triage filter — display only. Edit/exclude state stay in the row signals, so a row hidden
  // by the filter still commits (if included + committable). 'all' shows everything including excluded
  // rows (greyed in place). 'suggestions' surfaces the rows the warning dialog gates on.
  filterMode = signal<'all' | 'excluded' | 'duplicates' | 'needsfix' | 'suggestions'>('all');
  visibleLineItems = computed(() => {
    const rows = this.allRows();
    switch (this.filterMode()) {
      case 'excluded': return rows.filter((r) => r.excluded);
      case 'duplicates': return rows.filter((r) => r.duplicateSuspected);
      case 'needsfix': return rows.filter((r) => this.needsFix(r));
      case 'suggestions': return rows.filter((r) => this.isGhost(r));
      default: return rows;
    }
  });

  // A row can be committed once it carries a positive amount and a date (direction always has a value).
  rowCommittable = (row: LineItemEdit): boolean => {
    const amt = Number(row.amount);
    return row.amount !== '' && Number.isFinite(amt) && amt > 0 && !!row.date;
  };

  // An INCLUDED row that can't commit — a parsed un-mappable row or a half-filled manual one. These stay
  // in the manifest and BLOCK Confirm until fixed or excluded ("fix it or exclude it").
  needsFix = (row: LineItemEdit): boolean => !row.excluded && !this.rowCommittable(row);

  // ─── Selection (transient bulk-target only) ────────────────────────────────
  // Coupled to neither the include/exclude axis nor committability — every row is selectable so the user
  // can bulk-Include an excluded row or bulk-Set-direction a needs-fix one.
  isSelected(index: number): boolean {
    return this.rowSelection()[String(index)] === true;
  }

  private selectedRows = computed(() => this.allRows().filter((r) => this.isSelected(r.index)));
  selectionCount = computed(() => this.selectedRows().length);
  hasSelection = computed(() => this.selectionCount() > 0);

  // ─── Derived manifest sets ─────────────────────────────────────────────────
  // The commit set (and the checksum set): every included, committable row — parsed or manual.
  linesToImport = computed(() => this.allRows().filter((r) => !r.excluded && this.rowCommittable(r)));
  needsFixCount = computed(() => this.allRows().filter((r) => this.needsFix(r)).length);

  // ─── AI-suggestion (ghost) state ───────────────────────────────────────────
  // A ghost = an un-accepted AI suggestion (tier ②/③). It is never auto-applied and never learned; the
  // reviewer must accept it (per-row or "Accept all"), or the warning dialog gates the import.
  isGhost = (r: LineItemEdit): boolean =>
    r.payee === null && !r.dismissed
    && (r.suggestionSource === 'AiExistingPayee' || r.suggestionSource === 'AiNewPayee');

  suggestionCount = computed(() => this.allRows().filter((r) => this.isGhost(r)).length);
  hasSuggestions = computed(() => this.suggestionCount() > 0);

  // Ghosts that would import un-reviewed — only those on lines actually being committed (included +
  // committable). These are what the warning dialog counts and gates on.
  private pendingGhosts = computed(() =>
    this.linesToImport().filter((r) => this.isGhost(r)));
  pendingGhostCount = computed(() => this.pendingGhosts().length);

  private readonly _meta: StatementLineTableMeta = {
    patchRow: (index, patch) => this.patchRow(index, patch),
    setDirection: (index, direction) => this.setDirection(index, direction),
    onPayeeChange: (index, payee) => this.onRowPayeeChange(index, payee),
    acceptSuggestion: (index) => this.acceptSuggestion(index),
    dismissSuggestion: (index) => this.dismissSuggestion(index),
    toggleExcluded: (index) => this.toggleExcluded(index),
    resetRow: (index) => this.resetRow(index),
    deleteManualRow: (index) => this.deleteManualRow(index),
    // The cell hands us the row it already holds — compare directly, no allRows() scan.
    isDirty: (row) => this.isRowDirty(row),
  };

  private readonly _columns: ColumnDef<LineItemEdit>[] = [
    {
      id: 'select',
      header: () => flexRenderComponent(TableHeadSelection),
      cell: () => flexRenderComponent(TableRowSelection),
      enableSorting: false,
    },
    { id: 'date', header: 'Date', cell: () => flexRenderComponent(LineDateCell), enableSorting: false },
    { id: 'payee', header: 'Payee', cell: () => flexRenderComponent(LinePayeeCell), enableSorting: false },
    { id: 'type', header: 'Type', cell: () => flexRenderComponent(LineTypeCell), enableSorting: false },
    { id: 'description', header: 'Description', cell: () => flexRenderComponent(LineDescriptionCell), enableSorting: false },
    { id: 'amount', header: 'Amount', cell: () => flexRenderComponent(LineAmountCell), enableSorting: false },
    { id: 'actions', header: '', cell: () => flexRenderComponent(LineActionsCell), enableSorting: false },
  ];

  // Statement order is load-bearing (reconciliation / running-balance reading), so sorting stays off —
  // which also sidesteps any edit-vs-reorder hazard. Every row is selectable now (selection is transient
  // bulk-targeting, decoupled from the include/exclude axis and committability), so there's no
  // enableRowSelection gate — getCanSelect() defaults to true for every row.
  protected readonly table = createAngularTable<LineItemEdit>(() => ({
    data: this.visibleLineItems(),
    columns: this._columns,
    getRowId: (row) => String(row.index),
    enableSorting: false,
    onRowSelectionChange: (updater) => {
      this.rowSelection.set(updater instanceof Function ? updater(this.rowSelection()) : updater);
    },
    getCoreRowModel: getCoreRowModel(),
    state: { rowSelection: this.rowSelection() },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    select: 'w-9',
    date: 'w-48',
    amount: 'text-right',
    actions: 'w-10',
  };
  private readonly TD_CLASSES: Record<string, string> = {
    select: 'w-9 text-center align-middle',
    date: 'w-48 align-middle',
    amount: 'text-right align-middle',
    actions: 'w-10 text-center align-middle',
  };
  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }
  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? 'align-middle';
  }

  // Routes by the client row key's sign: manual rows (negative key) live in manualRows, parsed rows in
  // parsedRows. The staged-vs-manual split never leaves the browser.
  patchRow(index: number, patch: Partial<LineItemEdit>): void {
    const apply = (r: LineItemEdit): LineItemEdit =>
      r.index === index
        ? {
            ...r,
            ...patch,
            // Keep the memoized Date in lockstep, but only touch it when `date` actually changed.
            ...(patch.date !== undefined ? { dateObj: parseDate(patch.date) ?? undefined } : {}),
          }
        : r;
    if (index < 0) this.manualRows.update((rows) => rows.map(apply));
    else this.parsedRows.update((rows) => rows.map(apply));
  }

  // Apply a patch to every row (parsed + manual) matching a predicate — the containment point for bulk ops.
  private updateRows(
    pred: (r: LineItemEdit) => boolean,
    patchFn: (r: LineItemEdit) => Partial<LineItemEdit>,
  ): void {
    const apply = (rows: LineItemEdit[]) =>
      rows.map((r) => {
        if (!pred(r)) return r;
        const patch = patchFn(r);
        return {
          ...r,
          ...patch,
          ...(patch.date !== undefined ? { dateObj: parseDate(patch.date) ?? undefined } : {}),
        };
      });
    this.parsedRows.update(apply);
    this.manualRows.update(apply);
  }

  setDirection(index: number, direction: FlowDirection): void {
    this.patchRow(index, { direction });
  }

  // A manual pick / clear is an explicit gesture: set the payee, carry its default category chip, and flag
  // learn (so a picked payee teaches the mapping table). Clearing resets learn and the chip.
  onRowPayeeChange(index: number, p: PayeeResponse | null): void {
    this.patchRow(index, {
      payee: p ? { id: p.id, name: p.name } : null,
      category: p ? categoryOfPayee(p, this.autoCategoriseEnabled()) : null,
      learn: p !== null,
    });
  }

  // Accept one AI ghost (per-row). Existing-payee suggestions apply immediately; a new-payee suggestion
  // (tier ③) is created first, then applied. Either way it becomes an explicit, learned pick.
  acceptSuggestion(index: number): void {
    const row = this.allRows().find((r) => r.index === index);
    if (!row || !this.isGhost(row)) return;

    if (row.suggestionSource === 'AiExistingPayee' && row.suggestedPayeeId) {
      this.patchRow(index, {
        payee: { id: row.suggestedPayeeId, name: row.suggestedPayeeName ?? '' },
        category: row.suggestedCategory,
        learn: true,
      });
      return;
    }

    if (row.suggestionSource === 'AiNewPayee' && row.suggestedPayeeName) {
      this.createPayeeAndApply(row.suggestedPayeeName.trim(), [index], true);
    }
  }

  // Dismiss (reject) one AI ghost: it stops being a ghost, drops out of the suggestion count / import-gate,
  // and the line imports unattributed (the reviewer can still pick a payee manually afterwards).
  dismissSuggestion(index: number): void {
    const row = this.allRows().find((r) => r.index === index);
    if (!row || !this.isGhost(row)) return;
    this.patchRow(index, { dismissed: true });
  }

  // "Accept all suggestions (N)" — promote every ghost to a confirmed, learned pick. Existing-payee ghosts
  // apply synchronously; new-payee ghosts are created once per distinct name, then applied to every row that
  // proposed it (avoids minting duplicates within the batch).
  acceptAllSuggestions(): void {
    this.applyGhosts(this.allRows().filter((r) => this.isGhost(r)), true);
  }

  // Apply a set of ghosts. learn=true for explicit accepts; learn=false for the warning-dialog
  // apply-but-don't-learn path. onDone fires after any async payee creations settle.
  private applyGhosts(ghosts: LineItemEdit[], learn: boolean, onDone?: () => void): void {
    for (const r of ghosts) {
      if (r.suggestionSource === 'AiExistingPayee' && r.suggestedPayeeId) {
        this.patchRow(r.index, {
          payee: { id: r.suggestedPayeeId, name: r.suggestedPayeeName ?? '' },
          category: r.suggestedCategory,
          learn,
        });
      }
    }

    const newOnes = ghosts.filter((r) => r.suggestionSource === 'AiNewPayee' && !!r.suggestedPayeeName);
    const byName = new Map<string, number[]>();
    for (const r of newOnes) {
      const name = r.suggestedPayeeName!.trim();
      if (!name) continue;
      (byName.get(name) ?? byName.set(name, []).get(name)!).push(r.index);
    }

    if (byName.size === 0) {
      onDone?.();
      return;
    }

    const creations = [...byName.entries()].map(([name, indexes]) =>
      this.payeeApi.createPayee({ body: { name } }).pipe(
        map((payee) => ({ payee, indexes })),
        catchError(() => of(null)),
      ));

    forkJoin(creations).pipe(takeUntilDestroyed(this.destroyRef)).subscribe((results) => {
      for (const res of results) {
        if (!res) continue;
        for (const idx of res.indexes) {
          this.patchRow(idx, {
            payee: { id: res.payee.id, name: res.payee.name },
            category: categoryOfPayee(res.payee, this.autoCategoriseEnabled()),
            learn,
          });
        }
      }
      onDone?.();
    });
  }

  // Create a new payee then apply it to the given rows (used by per-row "Create & use").
  private createPayeeAndApply(name: string, indexes: number[], learn: boolean): void {
    if (!name) return;
    this.payeeApi.createPayee({ body: { name } }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: (payee) => {
        for (const idx of indexes) {
          this.patchRow(idx, {
            payee: { id: payee.id, name: payee.name },
            category: categoryOfPayee(payee, this.autoCategoriseEnabled()),
            learn,
          });
        }
      },
      error: (err: unknown) => this.errorHandler.handleError(err, 'Failed to create payee.'),
    });
  }

  // Selection writes go straight to the rowSelection signal — purely transient bulk-targeting.
  setSelected(index: number, selected: boolean): void {
    this.rowSelection.update((sel) => {
      const next = { ...sel };
      if (selected) next[String(index)] = true;
      else delete next[String(index)];
      return next;
    });
  }

  // Select / deselect every row in the current filter view (mirrors the header checkbox's scope).
  toggleAll(checked: boolean): void {
    const visible = this.visibleLineItems();
    this.rowSelection.update((sel) => {
      const next = { ...sel };
      for (const r of visible) {
        if (checked) next[String(r.index)] = true;
        else delete next[String(r.index)];
      }
      return next;
    });
  }

  clearSelection(): void {
    this.rowSelection.set({});
  }

  onFilterChange(value: unknown): void {
    if (value === 'all' || value === 'excluded' || value === 'duplicates'
      || value === 'needsfix' || value === 'suggestions')
      this.filterMode.set(value);
  }

  // Page-level "Review" — filter the table to the un-accepted ghosts. Distinct from the import-gate
  // dialog's reviewSuggestions(), which also closes that dialog.
  showSuggestions(): void {
    this.filterMode.set('suggestions');
  }

  // ─── Include / exclude axis ────────────────────────────────────────────────
  // Toggle a parsed row's excluded flag (manual rows Delete instead, so they never reach here).
  toggleExcluded(index: number): void {
    const row = this.parsedRows().find((r) => r.index === index);
    if (!row) return;
    this.patchRow(index, { excluded: !row.excluded });
  }

  // ─── Reset to original ─────────────────────────────────────────────────────
  private isRowDirty(row: LineItemEdit): boolean {
    const o = row.original;
    if (!o) return false;
    return row.amount !== o.amount
      || row.description !== o.description
      || row.direction !== o.direction
      || row.date !== o.date
      || (row.payee?.id ?? null) !== (o.payee?.id ?? null)
      || row.dismissed !== o.dismissed
      || row.learn !== o.learn
      || !sameCategory(row.category, o.category);
  }

  isDirty(index: number): boolean {
    const row = this.allRows().find((r) => r.index === index);
    return row ? this.isRowDirty(row) : false;
  }

  // Splat the captured original back over the row (re-deriving dateObj). Leaves `excluded` untouched — it
  // is an orthogonal axis. Manual rows have no original → no-op.
  resetRow(index: number): void {
    const apply = (rows: LineItemEdit[]) =>
      rows.map((r) => {
        const o = r.original;
        if (r.index !== index || !o) return r;
        return {
          ...r,
          amount: o.amount,
          description: o.description,
          direction: o.direction,
          date: o.date,
          dateObj: parseDate(o.date) ?? undefined,
          payee: o.payee,
          suggestionSource: o.suggestionSource,
          suggestedPayeeId: o.suggestedPayeeId,
          suggestedPayeeName: o.suggestedPayeeName,
          suggestedCategory: o.suggestedCategory,
          category: o.category,
          learn: o.learn,
          dismissed: o.dismissed,
        };
      });
    if (index < 0) this.manualRows.update(apply);
    else this.parsedRows.update(apply);
  }

  // ─── Manual rows ───────────────────────────────────────────────────────────
  // Append a blank manual row (negative key, no staged origin). It starts as a needs-fix row (empty
  // amount) → blocks Confirm until filled or deleted. Never learned (index null at commit).
  addManualRow(): void {
    const today = todayIso();
    const row: LineItemEdit = {
      index: this.nextManualKey--,
      amount: '',
      description: '',
      direction: 'Expense',
      date: today,
      dateObj: parseDate(today) ?? undefined,
      mappable: true,
      duplicateSuspected: false,
      dismissed: false,
      payee: null,
      suggestionSource: 'None',
      suggestedPayeeId: null,
      suggestedPayeeName: null,
      suggestedCategory: null,
      learn: false,
      category: null,
      manual: true,
      excluded: false,
      original: undefined,
    };
    this.manualRows.update((rows) => [...rows, row]);
  }

  deleteManualRow(index: number): void {
    this.manualRows.update((rows) => rows.filter((r) => r.index !== index));
    // Drop any lingering selection entry so rowSelection doesn't accumulate keys for gone rows.
    this.setSelected(index, false);
  }

  // ─── Bulk actions over the current selection (transient) ───────────────────
  private updateSelected(patch: (r: LineItemEdit) => Partial<LineItemEdit>): void {
    this.updateRows((r) => this.isSelected(r.index), patch);
  }

  bulkSetDirection(direction: FlowDirection): void {
    this.updateSelected(() => ({ direction }));
  }

  // Bulk exclude / include — sets the flag on selected PARSED rows only (manual rows Delete instead).
  bulkSetExcluded(excluded: boolean): void {
    this.updateRows((r) => this.isSelected(r.index) && !r.manual, () => ({ excluded }));
  }

  // Bulk-assign is scoped to selected lines that don't already have a confirmed payee — it fills the gaps
  // rather than overwriting deliberate picks / learned hits. An explicit gesture, so it learns and carries
  // the payee's category chip.
  bulkAssignPayee(p: PayeeResponse | null): void {
    if (!p) return;
    const category = categoryOfPayee(p, this.autoCategoriseEnabled());
    let skipped = 0;
    const apply = (rows: LineItemEdit[]) =>
      rows.map((r) => {
        if (!this.isSelected(r.index)) return r;
        if (r.payee !== null) { skipped++; return r; }
        // Manual rows never learn (index-less at commit), so keep their in-memory learn flag coherent.
        return { ...r, payee: { id: p.id, name: p.name }, category, learn: !r.manual };
      });
    this.parsedRows.update(apply);
    this.manualRows.update(apply);
    if (skipped > 0)
      toast.info(`Assigned to lines without a payee; ${skipped} with an existing payee were left unchanged.`);
  }

  bulkClearPayee(): void {
    this.updateSelected(() => ({ payee: null, category: null, learn: false }));
  }

  // ─── Advisory closing-balance checksum (never blocks commit) ──────────────
  // expected = opening + Σ(sign · amount) over included lines; sign follows the account-type convention
  // (asset: In +1 / Out −1; liability: Out +1 / In −1). Compared to the printed closing balance.
  checksum = computed<Checksum>(() => {
    const h = this.data()?.header;
    const opening = h?.openingBalance;
    const closing = h?.closingBalance;
    if (opening == null || closing == null) return { state: 'unavailable' };

    const isLiability = this.account()?.accountType === 'Liability';
    let sum = 0;
    for (const row of this.linesToImport()) {
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

  // Confirm is enabled when there's something to commit AND no included row is still un-committable — a
  // needs-fix row must be fixed or excluded first.
  canCommit = computed(() =>
    (this.balanceCommittable() || this.linesToImport().length > 0) && this.needsFixCount() === 0);

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

  // Import entry point: if any un-reviewed AI ghost would be committed, gate with the warning dialog; the
  // reviewer either goes back to review them or applies-without-learning. Otherwise commit straight through.
  commit(): void {
    if (!this.jobId() || !this.canCommit()) return;
    if (this.pendingGhostCount() > 0) {
      this.warnState.set('open');
      return;
    }
    this.doCommit();
  }

  // "Review them" — filter the table to the un-accepted ghosts so the reviewer can act on them.
  reviewSuggestions(): void {
    this.warnState.set('closed');
    this.filterMode.set('suggestions');
  }

  // "Apply all & import" — apply the pending ghosts WITHOUT learning (rubber-stamped, so not taught), then
  // commit once any new-payee creations settle.
  applyAllAndImport(): void {
    this.warnState.set('closed');
    this.applyGhosts(this.pendingGhosts(), false, () => this.doCommit());
  }

  private doCommit(): void {
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
        lineItems: this.linesToImport().map((r) => ({
          // A manual row has no staged origin → index null (never learned server-side). Parsed rows send
          // their staged index; the negative client key never leaves the browser.
          index: r.manual ? null : r.index,
          direction: r.direction,
          amount: Number(r.amount),
          date: r.date,
          description: r.description.trim() ? r.description.trim() : null,
          payeeId: r.payee?.id ?? null,
          // Teach the mapping table only for explicit picks on parsed rows (never for a rubber-stamped
          // tier-① pre-fill, an apply-but-don't-learn, or a manual row). Guard on a payee being set.
          learn: !r.manual && r.payee !== null && r.learn,
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
