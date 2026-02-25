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
  lucideX,
  lucideDownload,
  lucideTrash2,
  lucideFileText,
  lucideFileSearch,
  lucidePencil,
  lucideCheck,
  lucideTag,
  lucideCalendar,
  lucideHardDrive,
  lucideFileType,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { TagInputComponent } from '../tag-input/tag-input.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import { PartyInputComponent } from '../party-input/party-input.component';
import type { PartyOption } from '../party-input/party-input.component';
import { formatBytes } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { updateDocument } from '@/api/generated/fn/documents/update-document';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
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
    TagBadgeComponent,
    TagInputComponent,
    PartyBadgeComponent,
    PartyInputComponent,
  ],
  providers: [
    provideIcons({
      lucideX,
      lucideDownload,
      lucideTrash2,
      lucideFileText,
      lucideFileSearch,
      lucidePencil,
      lucideCheck,
      lucideTag,
      lucideCalendar,
      lucideHardDrive,
      lucideFileType,
      lucideUsers,
    }),
  ],
  template: `
    <hlm-sheet side="right" [state]="sheetState()" (stateChanged)="onSheetStateChanged($event)">
      <hlm-sheet-content *hlmSheetPortal="let ctx" [showCloseButton]="false"
        class="w-full max-w-md sm:max-w-md gap-0 p-0">

        @if (loading()) {
          <div class="flex h-full items-center justify-center">
            <hlm-spinner />
          </div>
        }

        @if (document()) {
          <!-- Header -->
          <div class="flex items-center gap-3 border-b border-border px-4 py-3">
            <div class="min-w-0 flex-1">
              @if (editing()) {
                <input
                  [value]="editName()"
                  (input)="editName.set(asInputValue($event))"
                  class="w-full rounded-md border border-input bg-background px-3 py-1.5 text-lg font-semibold text-foreground transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/20"
                />
              } @else {
                <h2 hlmSheetTitle class="truncate text-lg">
                  {{ document()!.name }}
                </h2>
              }
            </div>
            <button
              hlmBtn
              variant="ghost"
              size="icon-sm"
              hlmSheetClose
              aria-label="Close panel"
            >
              <ng-icon hlm name="lucideX" size="sm" />
            </button>
          </div>

          <!-- Preview -->
          <div class="border-b border-border bg-muted">
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

          <!-- Actions -->
          <div class="flex gap-2 border-b border-border px-4 py-3">
            @if (editing()) {
              <button hlmBtn (click)="handleSave()" [disabled]="saving()">
                <ng-icon hlm name="lucideCheck" size="sm" />
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
              <button hlmBtn variant="secondary" (click)="handleCancelEdit()" [disabled]="saving()">
                <ng-icon hlm name="lucideX" size="sm" />
                Cancel
              </button>
            } @else {
              <button hlmBtn variant="secondary" (click)="editing.set(true)">
                <ng-icon hlm name="lucidePencil" size="sm" />
                Edit
              </button>
              <a
                hlmBtn
                [href]="fileBlobUrl()"
                [download]="document()!.name"
              >
                <ng-icon hlm name="lucideDownload" size="sm" />
                Download
              </a>
              <button
                hlmBtn
                variant="ghost"
                (click)="handleDelete()"
                [disabled]="deleting()"
                class="text-destructive hover:bg-destructive/10 hover:text-destructive"
              >
                <ng-icon hlm name="lucideTrash2" size="sm" />
                {{ deleting() ? 'Deleting...' : 'Delete' }}
              </button>
            }
          </div>

          <!-- Tabs -->
          <hlm-tabs tab="info" class="flex flex-1 flex-col overflow-hidden">
            <hlm-tabs-list class="mx-4 mt-3">
              <button hlmTabsTrigger="info">Info</button>
              <button hlmTabsTrigger="text">Text</button>
            </hlm-tabs-list>
            <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
              <div class="space-y-4">
                <!-- Tags -->
                <div class="flex gap-3">
                  <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                    <ng-icon hlm name="lucideTag" size="sm" />
                    <span class="w-12 text-sm font-medium">Tags</span>
                  </div>
                  <div class="min-w-0 flex-1">
                    @if (editing()) {
                      <app-tag-input [value]="editTags()" (valueChange)="editTags.set($event)" />
                    } @else {
                      <div class="flex flex-wrap gap-1.5">
                        @if (document()!.tags && document()!.tags.length > 0) {
                          @for (tag of document()!.tags; track tag.id) {
                            <app-tag-badge [path]="tag.path" />
                          }
                        } @else {
                          <span class="text-sm text-muted-foreground">No tags</span>
                        }
                      </div>
                    }
                  </div>
                </div>

                <!-- Parties -->
                <div class="flex gap-3">
                  <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                    <ng-icon hlm name="lucideUsers" size="sm" />
                    <span class="w-12 text-sm font-medium">Parties</span>
                  </div>
                  <div class="min-w-0 flex-1">
                    @if (editing()) {
                      <app-party-input [value]="editParties()" (valueChange)="editParties.set($event)" />
                    } @else {
                      <div class="flex flex-wrap gap-1.5">
                        @if (document()!.parties && document()!.parties.length > 0) {
                          @for (party of document()!.parties; track party.id) {
                            <app-party-badge [name]="party.name" />
                          }
                        } @else {
                          <span class="text-sm text-muted-foreground">No parties</span>
                        }
                      </div>
                    }
                  </div>
                </div>

                <!-- Added -->
                <div class="flex gap-3">
                  <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                    <ng-icon hlm name="lucideCalendar" size="sm" />
                    <span class="w-12 text-sm font-medium">Added</span>
                  </div>
                  <div class="min-w-0 flex-1">
                    <span class="text-sm text-foreground">
                      {{ formatDate(document()!.createdAt) }}
                    </span>
                  </div>
                </div>

                <!-- Size -->
                <div class="flex gap-3">
                  <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                    <ng-icon hlm name="lucideHardDrive" size="sm" />
                    <span class="w-12 text-sm font-medium">Size</span>
                  </div>
                  <div class="min-w-0 flex-1">
                    <span class="text-sm text-foreground">
                      {{ formatBytes(+document()!.fileSize) }}
                    </span>
                  </div>
                </div>

                <!-- Type -->
                <div class="flex gap-3">
                  <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                    <ng-icon hlm name="lucideFileType" size="sm" />
                    <span class="w-12 text-sm font-medium">Type</span>
                  </div>
                  <div class="min-w-0 flex-1">
                    <span class="text-sm text-foreground">{{ document()!.contentType }}</span>
                  </div>
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
        }

      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class DocumentDetailPanelComponent implements OnDestroy {
  documentId = input<string | null>(null);
  closed = output<void>();
  refreshList = output<void>();

  editing = signal(false);
  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  saving = signal(false);
  deleting = signal(false);
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

  private fileBlob = toSignal(
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

    effect(() => {
      this.documentId();
      this.editing.set(false);
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
        this.editing.set(false);
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

  handleCancelEdit(): void {
    this.editing.set(false);
    const doc = this.document();
    if (doc) {
      this.editName.set(doc.name);
      this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
      this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
    }
  }

  handleDelete(): void {
    const id = this.documentId();
    if (!id) return;
    this.deleting.set(true);
    deleteDocument(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        this.deleting.set(false);
        this.refreshList.emit();
        this.closed.emit();
        toast.success('Document deleted.');
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete document.');
      },
    });
  }
}
