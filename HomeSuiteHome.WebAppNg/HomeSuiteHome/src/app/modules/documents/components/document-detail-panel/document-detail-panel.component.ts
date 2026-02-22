import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  inject,
  effect,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError } from 'rxjs';
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
} from 'lucide-angular';
import { SheetComponent } from '@/shared/components/ui/sheet/sheet.component';
import { TabsComponent, TabDefDirective } from '@/shared/components/ui/tabs/tabs.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { SpinnerComponent } from '@/shared/components/ui/spinner/spinner.component';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { TagInputComponent } from '../tag-input/tag-input.component';
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
                  class="w-full rounded-lg border border-border bg-surface px-3 py-1.5 text-lg font-semibold text-text-primary transition-colors focus:border-accent-500 focus:outline-none focus:ring-2 focus:ring-accent-500/20"
                />
              } @else {
                <h2 class="truncate text-lg font-semibold text-text-primary">
                  {{ document()!.name }}
                </h2>
              }
            </div>
            <button
              (click)="closed.emit()"
              class="shrink-0 cursor-pointer rounded-lg p-1 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
            >
              <lucide-icon [img]="icons.x" [size]="20" />
            </button>
          </div>

          <!-- Preview -->
          <div class="border-b border-border bg-surface-raised">
            @if (isPdf()) {
              <iframe [src]="fileUrl()" class="h-80 w-full" [title]="'Preview of ' + document()!.name"></iframe>
            } @else {
              <div
                class="flex h-48 flex-col items-center justify-center gap-2 text-text-secondary"
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
                [href]="fileUrl() + '?download=true'"
                download
                class="inline-flex items-center justify-center gap-2 rounded-lg bg-accent-500 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-600"
              >
                <lucide-icon [img]="icons.download" [size]="16" />
                Download
              </a>
              <app-button
                variant="ghost"
                (click)="handleDelete()"
                [disabled]="deleting()"
                className="text-red-500 hover:bg-red-50 hover:text-red-600"
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
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
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
                            <span class="text-sm text-text-muted">No tags</span>
                          }
                        </div>
                      }
                    </div>
                  </div>

                  <!-- Added -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
                      <lucide-icon [img]="icons.calendar" [size]="16" />
                      <span class="w-12 text-sm font-medium">Added</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-text-primary">
                        {{ formatDate(document()!.createdAt) }}
                      </span>
                    </div>
                  </div>

                  <!-- Size -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
                      <lucide-icon [img]="icons.hardDrive" [size]="16" />
                      <span class="w-12 text-sm font-medium">Size</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-text-primary">
                        {{ formatBytes(+document()!.fileSize) }}
                      </span>
                    </div>
                  </div>

                  <!-- Type -->
                  <div class="flex gap-3">
                    <div class="flex shrink-0 items-start gap-2 pt-0.5 text-text-secondary">
                      <lucide-icon [img]="icons.fileType" [size]="16" />
                      <span class="w-12 text-sm font-medium">Type</span>
                    </div>
                    <div class="min-w-0 flex-1">
                      <span class="text-sm text-text-primary">{{ document()!.contentType }}</span>
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
export class DocumentDetailPanelComponent {
  documentId = input<string | null>(null);
  closed = output<void>();
  refreshList = output<void>();

  editing = signal(false);
  editName = signal('');
  editTags = signal<string[]>([]);
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);

  icons = { x: X, download: Download, trash: Trash2, fileText: FileText, pencil: Pencil, check: Check, tag: Tag, calendar: Calendar, hardDrive: HardDrive, fileType: FileType };
  formatBytes = formatBytes;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  private documentData = toSignal(
    toObservable(this.documentId).pipe(
      switchMap((id) => {
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
      }
    });

    // Reset editing when documentId changes
    effect(() => {
      this.documentId();
      this.editing.set(false);
    });
  }

  isPdf(): boolean {
    return this.document()?.contentType === 'application/pdf';
  }

  fileUrl(): string {
    const id = this.documentId();
    return id ? `/api/v1/documents/${id}/file` : '';
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
      body: { name: this.editName(), tagPaths: this.editTags() },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.refreshList.emit();
        // Reload document data by re-triggering the observable
        const currentId = this.documentId();
        // Force a refetch by closing and reopening (a bit of a workaround)
        // Instead, we update the edit state from the new data
        if (currentId) {
          getDocument(this.http, this.apiConfig.rootUrl, { id: currentId }).subscribe({
            next: (r) => {
              const doc = r.body as GetDocumentResponse;
              this.editName.set(doc.name);
              this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
            },
          });
        }
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
