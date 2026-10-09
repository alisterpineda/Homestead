import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { combineLatest, of } from 'rxjs';
import { switchMap, map, catchError } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucideFileX2, lucideTrash2 } from '@ng-icons/lucide';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
import { DocumentPickerComponent } from '../../shared/document-picker/document-picker.component';
import type { TransactionDocumentLinkResponse } from '@/api/generated/models/transaction-document-link-response';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

/**
 * Shape-agnostic "Linked documents" dialog for a single transaction: lists links (deep-linking
 * into Documents), removes manual ones, and adds new manual links via the shared document
 * picker. Opened programmatically from the activity table's row actions — no trigger button.
 * Import-origin links render with a badge and a disabled remove (rollback owns their lifetime).
 */
@Component({
  selector: 'app-linked-documents-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmBadgeImports,
    HlmSpinner,
    NgIcon,
    RouterLink,
    DocumentPickerComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucideFileX2, lucideTrash2 })],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[560px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Linked documents</h2>
          <p hlmDialogDescription>
            Attach receipts, invoices, or statements from Documents to this transaction.
          </p>
        </div>

        @if (loading()) {
          <div class="flex justify-center py-6">
            <hlm-spinner />
          </div>
        } @else if (links().length > 0) {
          <ul class="divide-y divide-border rounded-md border border-border">
            @for (link of links(); track link.documentId) {
              <li class="flex items-center gap-2 px-3 py-2">
                @switch (link.availability) {
                  @case ('Available') {
                    <ng-icon name="lucideFileText" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
                    <a
                      [routerLink]="['/documents', link.documentId]"
                      (click)="ctx.close()"
                      class="min-w-0 truncate text-sm font-medium hover:underline"
                    >
                      {{ link.documentName }}
                    </a>
                  }
                  @case ('Trashed') {
                    <ng-icon name="lucideFileX2" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
                    <span class="min-w-0 truncate text-sm text-muted-foreground">
                      {{ link.documentName }}
                    </span>
                    <span class="shrink-0 text-xs text-muted-foreground">In trash</span>
                  }
                  @default {
                    <ng-icon name="lucideFileX2" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
                    <span class="min-w-0 truncate text-sm italic text-muted-foreground">
                      Not accessible
                    </span>
                  }
                }
                @if (link.origin === 'StatementImport') {
                  <span hlmBadge variant="secondary" class="ml-auto shrink-0">From statement import</span>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    type="button"
                    disabled
                    title="This link was created by a statement import. Roll back the import to remove it."
                    aria-label="Remove link (unavailable for statement imports)"
                  >
                    <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
                  </button>
                } @else {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    type="button"
                    class="ml-auto text-muted-foreground hover:text-destructive"
                    [disabled]="removing()"
                    (click)="onRemove(link)"
                    aria-label="Remove link"
                  >
                    <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
                  </button>
                }
              </li>
            }
          </ul>
        } @else {
          <p class="py-2 text-center text-sm text-muted-foreground">No documents linked yet.</p>
        }

        <div class="flex flex-col gap-1 pt-2">
          <span class="text-sm font-medium">Link a document</span>
          <app-document-picker [(search)]="search" (documentSelected)="onAdd($event)" />
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" type="button" hlmDialogClose>Close</button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class LinkedDocumentsDialogComponent {
  state = input<BrnDialogState>('closed');
  transactionId = input<string | null>(null);

  closed = output<void>();
  /** Emitted after any successful add/remove so the host can refresh row counts. */
  linksChanged = output<void>();

  search = signal('');
  loading = signal(false);
  removing = signal(false);
  adding = signal(false);

  private txApi = inject(FinancesTransactionApiService);
  private errorHandler = inject(ErrorHandlerService);
  private refreshTrigger = signal(0);

  readonly links = toSignal(
    combineLatest([toObservable(this.transactionId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of([] as TransactionDocumentLinkResponse[]);
        this.loading.set(true);
        return this.txApi.getTransactionDocuments({ transactionId: id }).pipe(
          catchError(() => of([] as TransactionDocumentLinkResponse[])),
        );
      }),
      map((links) => {
        this.loading.set(false);
        return links;
      }),
    ),
    { initialValue: [] as TransactionDocumentLinkResponse[] },
  );

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.search.set('');
      // No refresh bump here: the host clears transactionId on close, so every open arrives as an
      // id change and fetches exactly once (a bump here would race the id change into two requests).
    } else {
      this.closed.emit();
    }
  }

  onAdd(doc: GetDocumentsResponse): void {
    const id = this.transactionId();
    if (!id || this.adding()) return;
    this.adding.set(true);
    this.txApi
      .addTransactionDocumentLink({ transactionId: id, body: { documentId: doc.id } })
      .pipe(handleMutationError(this.errorHandler, this.adding, 'Failed to link document.'))
      .subscribe(() => {
        this.adding.set(false);
        this.search.set('');
        this.refreshTrigger.update((v) => v + 1);
        this.linksChanged.emit();
      });
  }

  onRemove(link: TransactionDocumentLinkResponse): void {
    const id = this.transactionId();
    if (!id || this.removing()) return;
    this.removing.set(true);
    this.txApi
      .removeTransactionDocumentLink({ transactionId: id, documentId: link.documentId })
      .pipe(handleMutationError(this.errorHandler, this.removing, 'Failed to remove link.'))
      .subscribe(() => {
        this.removing.set(false);
        this.refreshTrigger.update((v) => v + 1);
        this.linksChanged.emit();
      });
  }
}
