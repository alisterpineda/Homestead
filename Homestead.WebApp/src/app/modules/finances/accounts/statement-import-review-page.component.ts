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
} from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { PayeePickerComponent } from '../shared/payee-picker/payee-picker.component';
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

// Client-side edit state for one proposed line — the review page owns this until commit. `mappable` echoes
// the server's un-mappable flag (missing amount/direction); `date`/`amount` are strings for uncontrolled inputs.
interface LineItemEdit {
  index: number;
  accepted: boolean;
  amount: string;
  description: string;
  direction: FlowDirection;
  date: string;
  mappable: boolean;
  runningBalance: number | null;
  duplicateSuspected: boolean;
  // Client-side payee pick, lost on reload (not staged server-side) — accepted per the plan. Held as a
  // single stable object so the per-row picker's [value] doesn't get a new identity every change detection.
  payee: { id: string; name: string } | null;
}

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
    HlmCheckboxImports,
    HlmIconImports,
    HlmResizableImports,
    HlmTabsImports,
    HlmEmptyImports,
    HlmDatePicker,
    HlmDatePickerInput,
    PayeePickerComponent,
  ],
  providers: [provideIcons({
    lucideCircleAlert, lucideCircleCheck, lucideTriangleAlert, lucideCircleHelp, lucideFileQuestion,
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
              <div class="flex items-center justify-between">
                <h3 class="text-sm font-medium">Transactions ({{ lineItems().length }})</h3>
                <span class="text-xs text-muted-foreground">{{ acceptedLines().length }} selected</span>
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

              <div class="overflow-x-auto rounded-md border">
                <table class="w-full border-collapse text-sm">
                  <thead>
                    <tr class="border-b bg-muted/40 text-xs text-muted-foreground">
                      <th class="w-9 p-2 text-center">
                        <hlm-checkbox
                          [checked]="allSelected()"
                          [indeterminate]="someSelected()"
                          (checkedChange)="toggleAll($event === true)"
                          [aria-label]="'Select all lines'"
                        />
                      </th>
                      <th class="p-2 text-left font-medium">Date</th>
                      <th class="p-2 text-left font-medium">Description</th>
                      <th class="p-2 text-left font-medium">Type</th>
                      <th class="p-2 text-left font-medium">Payee</th>
                      <th class="p-2 text-right font-medium">Amount</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of lineItems(); track row.index; let i = $index) {
                      <tr class="border-b last:border-0 align-middle" [class.opacity-50]="!rowCommittable(row)">
                        <td class="p-2 text-center">
                          <hlm-checkbox
                            [checked]="row.accepted"
                            [disabled]="!rowCommittable(row)"
                            (checkedChange)="setRowAccepted(i, $event === true)"
                            [aria-label]="'Accept transaction line'"
                          />
                        </td>
                        <td class="p-2">
                          <input
                            hlmInput
                            type="date"
                            class="w-36"
                            [value]="row.date"
                            (input)="patchRow(i, { date: asValue($event) })"
                          />
                        </td>
                        <td class="p-2">
                          <input
                            hlmInput
                            class="w-40"
                            maxlength="512"
                            [value]="row.description"
                            (input)="patchRow(i, { description: asValue($event) })"
                          />
                        </td>
                        <td class="p-2">
                          <button
                            type="button"
                            (click)="toggleRowDirection(i)"
                            title="Click to switch between Expense and Income"
                            class="rounded-md border px-2 py-0.5 text-xs font-medium"
                            [class.text-emerald-600]="row.direction === 'Income'"
                            [class.border-emerald-500/40]="row.direction === 'Income'"
                            [class.text-muted-foreground]="row.direction === 'Expense'"
                          >
                            {{ row.direction }}
                          </button>
                        </td>
                        <td class="p-2">
                          <app-payee-picker
                            [value]="row.payee"
                            ariaLabel="Select payee for this line"
                            (valueChange)="onRowPayeeChange(i, $event)"
                          />
                        </td>
                        <td class="p-2 text-right">
                          <input
                            hlmInput
                            type="number"
                            step="0.01"
                            min="0"
                            class="w-24 text-right"
                            [value]="row.amount"
                            (input)="patchRow(i, { amount: asValue($event) })"
                          />
                        </td>
                      </tr>
                      @if (row.duplicateSuspected) {
                        <tr class="border-b last:border-0">
                          <td></td>
                          <td colspan="5" class="px-2 pb-2 text-xs text-amber-600">
                            <ng-icon hlm name="lucideTriangleAlert" size="xs" /> Possible duplicate of an existing transaction.
                          </td>
                        </tr>
                      }
                    }
                  </tbody>
                </table>
              </div>
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

  // Per-row line-item edit state. Un-mappable rows (server flag) seed unchecked so the reviewer must
  // deliberately opt them in after editing; mappable rows keep the server's default-accepted.
  lineItems = seededLinkedSignal<string, LineItemEdit[]>(this.seedKey, () =>
    (this.data()?.lineItems ?? []).map((r) => ({
      index: r.index,
      accepted: r.mappable ? r.accepted : false,
      amount: String(r.amount),
      description: r.description ?? '',
      direction: r.direction,
      date: r.date ?? '',
      mappable: r.mappable,
      runningBalance: r.runningBalance ?? null,
      duplicateSuspected: r.duplicateSuspected,
      payee: null,
    })),
  );

  // A row can be committed once it carries a positive amount and a date (direction always has a value).
  rowCommittable = (row: LineItemEdit): boolean => {
    const amt = Number(row.amount);
    return row.amount !== '' && Number.isFinite(amt) && amt > 0 && !!row.date;
  };

  acceptedLines = computed(() => this.lineItems().filter((r) => r.accepted && this.rowCommittable(r)));

  allSelected = computed(() => {
    const committable = this.lineItems().filter((r) => this.rowCommittable(r));
    return committable.length > 0 && committable.every((r) => r.accepted);
  });
  someSelected = computed(() => this.lineItems().some((r) => r.accepted) && !this.allSelected());

  patchRow(i: number, patch: Partial<LineItemEdit>): void {
    this.lineItems.update((rows) => rows.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
  }

  setRowAccepted(i: number, accepted: boolean): void {
    this.patchRow(i, { accepted });
  }

  toggleRowDirection(i: number): void {
    this.lineItems.update((rows) =>
      rows.map((r, idx) =>
        idx === i ? { ...r, direction: r.direction === 'Expense' ? 'Income' : 'Expense' } : r));
  }

  onRowPayeeChange(i: number, p: PayeeResponse | null): void {
    this.patchRow(i, { payee: p ? { id: p.id, name: p.name } : null });
  }

  toggleAll(checked: boolean): void {
    this.lineItems.update((rows) =>
      rows.map((r) => ({ ...r, accepted: checked && this.rowCommittable(r) })));
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
