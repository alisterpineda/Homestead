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
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { isoDateBinding } from '@/shared/utils/date-picker-config';
import { toast } from '@spartan-ng/brain/sonner';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { todayIso } from '../shared/date';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';

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
    HlmDatePicker,
    HlmDatePickerInput,
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
              Confirm the closing balance below. It'll be recorded as a balance assertion on this account.
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

          @if (d.duplicateAssertionExists) {
            <div class="flex items-center gap-2 rounded-md border border-amber-500/50 px-3 py-2 text-sm text-amber-600">
              <ng-icon hlm name="lucideTriangleAlert" size="sm" />
              <span>A balance assertion already exists for this date.</span>
            </div>
          }

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

              <div class="flex items-center justify-end gap-2 pt-2">
                <button hlmBtn variant="ghost" (click)="backToAccount()" [disabled]="committing()">
                  Cancel
                </button>
                <button hlmBtn (click)="commit()" [disabled]="!canCommit() || committing()">
                  {{ committing() ? 'Committing…' : 'Commit balance' }}
                </button>
              </div>
            </div>
          } @else {
            <div class="flex flex-col gap-3">
              <div class="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
                No closing balance was found in this statement. There's nothing to import.
              </div>
              <div class="flex justify-end">
                <button hlmBtn variant="outline" (click)="backToAccount()">Back to account</button>
              </div>
            </div>
          }
        </div>
      }
    </ng-template>
  `,
})
export class StatementImportReviewPageComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private statementApi = inject(FinancesStatementApiService);
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

  canCommit = computed(() => {
    if (!this.data()?.proposedBalanceEntry) return false;
    if (!this.date()) return false;
    const amt = Number(this.amount());
    return this.amount() !== '' && Number.isFinite(amt) && amt >= 0;
  });

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

    this.committing.set(true);
    this.statementApi.commit({
      jobId,
      body: {
        assertedAmount: Number(this.amount()),
        date: this.date(),
        memo: this.memo().trim() ? this.memo().trim() : null,
        accepted: true,
      },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.committing, 'Failed to commit balance.'),
    ).subscribe({
      next: (result) => {
        this.committing.set(false);
        toast.success('Balance recorded.');
        void this.router.navigate(['/finances/accounts', result.accountId]);
      },
    });
  }
}
