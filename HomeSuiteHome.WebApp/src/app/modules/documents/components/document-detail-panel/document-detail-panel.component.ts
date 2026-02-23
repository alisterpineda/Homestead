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
import { LucideAngularModule } from 'lucide-angular';
import {
  X,
  Download,
  Trash2,
  FileText,
  Pencil,
  Check,
  Tag,
  Calendar,
  HardDrive,
  FileType,
  Users,
} from 'lucide-angular';
import { SheetComponent } from '@/shared/components/ui/sheet/sheet.component';
import { TabsComponent, TabDefDirective } from '@/shared/components/ui/tabs/tabs.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { SpinnerComponent } from '@/shared/components/ui/spinner/spinner.component';
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

@Component({
  selector: 'app-document-detail-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LucideAngularModule,
    SheetComponent,
    TabsComponent,
    TabDefDirective,
    ButtonComponent,
    SpinnerComponent,
    TagBadgeComponent,
    TagInputComponent,
    PartyBadgeComponent,
    PartyInputComponent,
  ],
  template: `
    <app-sheet [open]="!!documentId()" (closed)="closed.emit()">
      @if (loading()) {
        <div class="flex h-full items-center justify-center">
          <app-spinner size="lg" />
        </div>
      }

      @if (document()) {
        <div class="flex h-full flex-col">
          <!-- Header -->
          <div class="flex items-center gap-3 border-b border-border px-4 py-3">
            <div class="min-w-0 flex-1">
              @if (editing()) {
                <input
                  [value]="editName()"
                  (input)="editName.set(asInputValue($event))"
                  class="w-full rounded-lg border border-border bg-background px-3 py-1.5 text-lg font-semibold text-foreground transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/20"
                />
              } @else {
                <h2 class="truncate text-lg font-semibold text-foreground">
                  {{ document()!.name }}
                </h2>
              }
            </div>
            <button
              (click)="closed.emit()"
              class="shrink-0 cursor-pointer rounded-lg p-1 text-muted-foreground hover:bg-accent hover:text-accent-foreground"
            >
              <lucide-icon [img]="icons.x" [size]="20" />
            </button>
          </div>

          <!-- Preview -->
          <div class="border-b border-border bg-muted">
            @if (isPdf()) {
              <iframe [src]="safeFileUrl()" class="h-80 w-full" [title]="'Preview of ' + document()!.name"></iframe>
            } @else {
              <div
                class="flex h-48 flex-col items-center justify-center gap-2 text-muted-foreground"
              >
                <lucide-icon [img]="icons.fileText" [size]="48" [strokeWidth]="1" />
                <span class="text-sm">Preview not available</span>
              </div>
            }
          </div>

          <!-- Actions -->
          <div class="flex gap-2 border-b border-border px-4 py-3">
            @if (editing()) {
              <app-button variant="primary" (click)="handleSave()" [disabled]="saving()">
                <lucide-icon [img]="icons.check" [size]="16" />
                {{ saving() ? 'Saving...' : 'Save' }}
              </app-button>
              <app-button variant="secondary" (click)="handleCancelEdit()" [disabled]="saving()">
                <lucide-icon [img]="icons.x" [size]="16" />
                Cancel
              </app-button>
            } @else {
              <app-button variant="secondary" (click)="editing.set(true)">
                <lucide-icon [img]="icons.pencil" [size]="16" />
                Edit
              </app-button>
              <a
                [href]="fileBlobUrl()"
                [download]="document()!.name"
                class="inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
              >
                <lucide-icon [img]="icons.download" [size]="16" />
                Download
              </a>
              <app-button
                variant="ghost"
                (click)="handleDelete()"
                [disabled]="deleting()"
                className="text-destructive hover:bg-destructive/10 hover:text-destructive"
              >
                <lucide-icon [img]="icons.trash" [size]="16" />
                {{ deleting() ? 'Deleting...' : 'Delete' }}
              </app-button>
            }
          </div>

          <!-- Tabs -->
          <div class="flex-1 overflow-y-auto px-4 py-3">
            <app-tabs>
              <ng-template appTabDef="info" label="Info">
                <div class="space-y-4">
                  <!-- Tags -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-muted-foreground">
                      <lucide-icon [img]="icons.tag" [size]="16" />
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
                      <lucide-icon [img]="icons.users" [size]="16" />
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
                      <lucide-icon [img]="icons.calendar" [size]="16" />
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
                      <lucide-icon [img]="icons.hardDrive" [size]="16" />
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
                      <lucide-icon [img]="icons.fileType" [size]="16" />
                      <span class="w-12 text-sm font-medium">Type</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-foreground">{{ document()!.contentType }}</span>
                    </div>
                  </div>
                </div>
              </ng-template>
            </app-tabs>
          </div>
        </div>
      }
    </app-sheet>
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

  icons = { x: X, download: Download, trash: Trash2, fileText: FileText, pencil: Pencil, check: Check, tag: Tag, calendar: Calendar, hardDrive: HardDrive, fileType: FileType, users: Users };
  formatBytes = formatBytes;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private sanitizer = inject(DomSanitizer);

  fileBlobUrl = signal<string | null>(null);

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
    // Sync edit state when document changes
    effect(() => {
      const doc = this.document();
      if (doc) {
        this.editName.set(doc.name);
        this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
        this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
      }
    });

    // Reset editing when documentId changes
    effect(() => {
      this.documentId();
      this.editing.set(false);
    });
  }

  ngOnDestroy(): void {
    this.revokeCurrentBlobUrl();
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
      },
      error: () => {
        this.saving.set(false);
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
      },
      error: () => {
        this.deleting.set(false);
      },
    });
  }
}
