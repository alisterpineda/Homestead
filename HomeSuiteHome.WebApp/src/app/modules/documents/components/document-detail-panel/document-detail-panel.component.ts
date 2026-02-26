import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  inject,
  effect,
  computed,
  OnDestroy,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, tap } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFileText,
  lucideFileSearch,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { PartyInputComponent } from '../party-input/party-input.component';
import type { PartyOption } from '../party-input/party-input.component';
import { formatBytes } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { updateDocument } from '@/api/generated/fn/documents/update-document';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-detail-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
    HlmSheetImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
  ],
  providers: [
    provideIcons({
      lucideFileText,
      lucideFileSearch,
    }),
  ],
  template: `
    <hlm-sheet side="right" [state]="sheetState()" (stateChanged)="onSheetStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal="let ctx"
        class="w-full max-w-md sm:max-w-md gap-0 p-0">

        @if (loading()) {
          <div class="flex h-full items-center justify-center">
            <hlm-spinner />
          </div>
        }

        @if (document()) {
          <!-- Header -->
          <div hlmSheetHeader class="pr-10">
            <h2 hlmSheetTitle class="truncate text-lg">
              {{ document()!.name }}
            </h2>
          </div>

          <!-- Preview -->
          <div class="bg-muted">
            @if (isPdf()) {
              <iframe
                [src]="safeFileUrl()"
                class="h-80 w-full"
                [title]="'Preview of ' + document()!.name"
              ></iframe>
            } @else {
              <div
                class="flex h-48 flex-col items-center justify-center gap-2 text-muted-foreground"
              >
                <ng-icon hlm name="lucideFileText" size="xl" />
                <span class="text-sm">Preview not available</span>
              </div>
            }
          </div>

          <!-- Tabs -->
          <hlm-tabs tab="info" class="flex flex-1 flex-col overflow-hidden">
            <hlm-tabs-list variant="line" class="mx-4 mt-3">
              <button hlmTabsTrigger="info">Info</button>
              <button hlmTabsTrigger="text">Text</button>
            </hlm-tabs-list>
            <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
              <div class="space-y-4">
                <!-- Name -->
                <hlm-form-field>
                  <label hlmLabel for="doc-name">Name</label>
                  <input
                    hlmInput
                    id="doc-name"
                    [value]="editName()"
                    (input)="editName.set(asInputValue($event))"
                  />
                </hlm-form-field>

                <!-- Tags -->
                <div class="space-y-2">
                  <label hlmLabel>Tags</label>
                  <app-tag-input [value]="editTags()" (valueChange)="editTags.set($event)" />
                </div>

                <!-- Parties -->
                <div class="space-y-2">
                  <label hlmLabel>Parties</label>
                  <app-party-input [value]="editParties()" (valueChange)="editParties.set($event)" />
                </div>

                <!-- Added -->
                <div class="space-y-2">
                  <label hlmLabel>Added</label>
                  <p class="text-sm text-muted-foreground">
                    {{ formatDate(document()!.createdAt) }}
                  </p>
                </div>

                <!-- Size -->
                <div class="space-y-2">
                  <label hlmLabel>Size</label>
                  <p class="text-sm text-muted-foreground">
                    {{ formatBytes(+document()!.fileSize) }}
                  </p>
                </div>

                <!-- Type -->
                <div class="space-y-2">
                  <label hlmLabel>Type</label>
                  <p class="text-sm text-muted-foreground">{{ document()!.contentType }}</p>
                </div>
              </div>
            </div>
            <div hlmTabsContent="text" class="flex-1 overflow-y-auto px-4 py-3">
              @if (document()!.extractedText) {
                <div class="max-h-96 overflow-y-auto rounded-md border border-border bg-muted p-4">
                  <pre class="whitespace-pre-wrap text-sm text-foreground">{{ document()!.extractedText }}</pre>
                </div>
              } @else {
                <div class="flex flex-col items-center justify-center gap-2 py-8 text-muted-foreground">
                  <ng-icon hlm name="lucideFileSearch" size="xl" />
                  <span class="text-sm">No extracted text available</span>
                </div>
              }
            </div>
          </hlm-tabs>

          <!-- Footer -->
          <div hlmSheetFooter>
            <button hlmBtn (click)="handleSave()" [disabled]="!hasChanges() || saving()">
              {{ saving() ? 'Saving...' : 'Save changes' }}
            </button>
          </div>
        }

      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class DocumentDetailPanelComponent implements OnDestroy {
  documentId = input<string | null>(null);
  closed = output<void>();
  refreshList = output<void>();

  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  saving = signal(false);
  loading = signal(false);
  private refreshTrigger = signal(0);

  formatBytes = formatBytes;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private sanitizer = inject(DomSanitizer);

  fileBlobUrl = signal<string | null>(null);

  sheetState = computed<'open' | 'closed'>(() => this.documentId() ? 'open' : 'closed');

  safeFileUrl = computed(() => {
    const url = this.fileBlobUrl();
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });

  hasChanges = computed(() => {
    const doc = this.document();
    if (!doc) return false;
    if (this.editName() !== doc.name) return true;
    const origTags = (doc.tags?.map(t => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i])) return true;
    const origPartyIds = (doc.parties?.map(p => p.id) ?? []).slice().sort();
    const currPartyIds = this.editParties().map(p => p.id).slice().sort();
    if (origPartyIds.length !== currPartyIds.length || origPartyIds.some((id, i) => id !== currPartyIds[i])) return true;
    return false;
  });

  private _fileBlob = toSignal(
    combineLatest([toObservable(this.documentId), toObservable(this.refreshTrigger)]).pipe(
      tap(() => this.revokeCurrentBlobUrl()),
      switchMap(([id]) => {
        if (!id) return of(null);
        return this.http.get(`/api/v1/documents/${id}/file`, { responseType: 'blob' }).pipe(
          map((blob) => URL.createObjectURL(blob)),
          catchError(() => of(null)),
        );
      }),
      tap((url) => this.fileBlobUrl.set(url)),
    ),
    { initialValue: null },
  );

  private documentData = toSignal(
    combineLatest([toObservable(this.documentId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        return getDocument(this.http, this.apiConfig.rootUrl, { id }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetDocumentResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  document = this.documentData;

  constructor() {
    effect(() => {
      const doc = this.document();
      if (doc) {
        this.editName.set(doc.name);
        this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
        this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
      }
    });
  }

  ngOnDestroy(): void {
    this.revokeCurrentBlobUrl();
  }

  onSheetStateChanged(state: string): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }

  isPdf(): boolean {
    return this.document()?.contentType === 'application/pdf';
  }

  private revokeCurrentBlobUrl(): void {
    const url = this.fileBlobUrl();
    if (url) {
      URL.revokeObjectURL(url);
      this.fileBlobUrl.set(null);
    }
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleSave(): void {
    const id = this.documentId();
    if (!id) return;
    this.saving.set(true);
    updateDocument(this.http, this.apiConfig.rootUrl, {
      id,
      body: {
        name: this.editName(),
        tagPaths: this.editTags(),
        partyIds: this.editParties().map((p) => p.id),
      },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.refreshList.emit();
        this.refreshTrigger.update((v) => v + 1);
        toast.success('Document updated.');
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to save changes.');
      },
    });
  }
}
