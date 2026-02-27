import {
  Component,
  ChangeDetectionStrategy,
  signal,
  inject,
  effect,
  untracked,
  computed,
  OnDestroy,
  viewChild,
  afterNextRender,
  TemplateRef,
} from '@angular/core';
import { Location, NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, forkJoin, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFileText,
  lucideFileSearch,
  lucideDownload,
  lucideTrash2,
  lucideSave,
  lucideEllipsisVertical,
  lucideFilePlus2,
  lucideChevronDown,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { TagInputComponent } from '../../components/tag-input/tag-input.component';
import { PartyInputComponent } from '../../components/party-input/party-input.component';
import type { PartyOption } from '../../components/party-input/party-input.component';
import { AddVersionDialogComponent } from '../../components/add-version-dialog/add-version-dialog.component';
import { formatBytes } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { getDocumentVersion } from '@/api/generated/fn/documents/get-document-version';
import { updateDocument } from '@/api/generated/fn/documents/update-document';
import { updateDocumentVersion } from '@/api/generated/fn/documents/update-document-version';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { GetDocumentVersionResponse } from '@/api/generated/models/get-document-version-response';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
    HlmResizableImports,
    HlmBreadCrumbImports,
    HlmDropdownMenuImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
    HlmTextareaImports,
    HlmCollapsibleImports,
    HlmSeparatorImports,
    AddVersionDialogComponent,
  ],
  providers: [
    provideIcons({
      lucideFileText,
      lucideFileSearch,
      lucideDownload,
      lucideTrash2,
      lucideSave,
      lucideEllipsisVertical,
      lucideFilePlus2,
      lucideChevronDown,
    }),
  ],
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/documents">Documents</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          @if (hasMultipleVersions()) {
            <li hlmBreadcrumbItem class="min-w-0">
              <button
                 (click)="navigateToLatestVersion()"
                 [title]="document()?.name ?? 'Document'"
                 class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px] hover:text-foreground transition-colors">
                {{ document()?.name ?? 'Document' }}
              </button>
            </li>
            <li hlmBreadcrumbSeparator></li>
            <li hlmBreadcrumbItem>
              <button class="flex items-center gap-1" [hlmDropdownMenuTrigger]="versionMenu">
                <span hlmBreadcrumbPage>{{ selectedVersionLabel() }}</span>
                <ng-icon hlm size="xs" name="lucideChevronDown" />
              </button>
              <ng-template #versionMenu>
                <hlm-dropdown-menu>
                  @for (rev of sortedVersions(); track rev.id; let first = $first) {
                    <button hlmDropdownMenuItem (triggered)="onVersionSelected(rev.id)">
                      {{ first ? (rev.label ? 'Latest - ' + rev.label : 'Latest') : versionLabel(rev) }}
                    </button>
                  }
                </hlm-dropdown-menu>
              </ng-template>
            </li>
          } @else {
            <li hlmBreadcrumbItem class="min-w-0">
              <span hlmBreadcrumbPage
                    [title]="document()?.name ?? 'Document'"
                    class="truncate max-w-[150px] sm:max-w-[250px] md:max-w-[350px]">
                {{ document()?.name ?? 'Document' }}
              </span>
            </li>
          }
        </ol>
      </nav>
    </ng-template>

    @if (loading()) {
      <div class="flex h-full items-center justify-center">
        <hlm-spinner />
      </div>
    }

    @if (document(); as doc) {
      <div class="flex items-center gap-2 mb-4">
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn size="sm" variant="outline" (click)="handleDownload()">
            <ng-icon hlm name="lucideDownload" size="sm" />
            Download
          </button>
          <button hlmBtn size="sm" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            <ng-icon hlm name="lucideSave" size="sm" />
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
          <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="moreMenu">
            <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
          </button>
          <ng-template #moreMenu>
            <hlm-dropdown-menu class="w-48">
              <button hlmDropdownMenuItem (triggered)="addVersionDialog.open()">
                <ng-icon hlm name="lucideFilePlus2" size="sm" />
                <span>Add Version</span>
              </button>
              <button hlmDropdownMenuItem variant="destructive" (triggered)="handleDelete()" [disabled]="deleting()">
                <ng-icon hlm name="lucideTrash2" size="sm" />
                <span>{{ deleting() ? 'Deleting...' : 'Delete' }}</span>
              </button>
            </hlm-dropdown-menu>
          </ng-template>
        </div>
      </div>

      <app-add-version-dialog
        #addVersionDialog
        [documentId]="doc.id"
        (versionCreated)="refreshTrigger.update(v => v + 1)"
      />

      <!-- Shared content templates -->
      <ng-template #previewContent>
        @if (isPdf() && safeFileUrl()) {
          <iframe
            [src]="safeFileUrl()"
            class="h-full w-full"
            [title]="'Preview of ' + doc.name"
          ></iframe>
        } @else if (isImage() && fileBlobUrl()) {
          <div class="flex h-full w-full items-center justify-center bg-black">
            <img
              [src]="fileBlobUrl()"
              [alt]="doc.name"
              class="checkerboard max-h-full max-w-full"
            />
          </div>
        } @else if (!fileBlobUrl() && (isPdf() || isImage())) {
          <div class="flex h-full items-center justify-center">
            <hlm-spinner />
          </div>
        } @else {
          <div
            class="flex h-full flex-col items-center justify-center gap-2 bg-muted text-muted-foreground"
          >
            <ng-icon hlm name="lucideFileText" size="xl" />
            <span class="text-sm">Preview not available</span>
          </div>
        }
      </ng-template>

      <ng-template #infoContent>
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

          <!-- Document Date -->
          <hlm-form-field>
            <label hlmLabel for="doc-date">Document Date</label>
            <input
              hlmInput
              type="date"
              id="doc-date"
              [value]="editDocumentDate()"
              (input)="editDocumentDate.set(asInputValue($event))"
            />
          </hlm-form-field>

          <!-- Valid From / Valid Until -->
          <div class="grid grid-cols-2 gap-3">
            <hlm-form-field>
              <label hlmLabel for="doc-valid-from">Valid From</label>
              <input
                hlmInput
                type="date"
                id="doc-valid-from"
                [value]="editValidFrom()"
                (input)="editValidFrom.set(asInputValue($event))"
              />
            </hlm-form-field>
            <hlm-form-field>
              <label hlmLabel for="doc-valid-until">Valid Until</label>
              <input
                hlmInput
                type="date"
                id="doc-valid-until"
                [value]="editValidUntil()"
                (input)="editValidUntil.set(asInputValue($event))"
              />
            </hlm-form-field>
          </div>

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
              {{ formatDate(doc.createdAt) }}
            </p>
          </div>

          <!-- Version Details -->
          <hlm-separator />

          <div hlmCollapsible class="group">
            <button hlmCollapsibleTrigger
              class="flex w-full items-center justify-between py-1 text-sm font-medium hover:underline">
              Version Details
              <ng-icon hlm size="sm" name="lucideChevronDown"
                class="transition-transform duration-200 group-data-[state=open]:rotate-180" />
            </button>
            <div hlmCollapsibleContent>
              <div class="space-y-4 pt-3">
                <hlm-form-field>
                  <label hlmLabel for="version-label">Label</label>
                  <input hlmInput id="version-label" maxlength="256"
                    placeholder="e.g., Final Draft, Signed Copy"
                    [value]="editVersionLabel()"
                    (input)="editVersionLabel.set(asInputValue($event))" />
                </hlm-form-field>
                <hlm-form-field>
                  <label hlmLabel for="version-notes">Notes</label>
                  <textarea hlmTextarea id="version-notes" maxlength="2048" rows="3"
                    placeholder="Version notes..."
                    [value]="editVersionNotes()"
                    (input)="editVersionNotes.set(asInputValue($event))"></textarea>
                </hlm-form-field>
                <!-- Added -->
                @if (displayVersionCreatedAt()) {
                  <div class="space-y-2">
                    <label hlmLabel>Added</label>
                    <p class="text-sm text-muted-foreground">
                      {{ formatDate(displayVersionCreatedAt()) }}
                    </p>
                  </div>
                }
                <!-- Size -->
                <div class="space-y-2">
                  <label hlmLabel>Size</label>
                  <p class="text-sm text-muted-foreground">
                    {{ formatBytes(+displayFileSize()) }}
                  </p>
                </div>
                <!-- Type -->
                <div class="space-y-2">
                  <label hlmLabel>Type</label>
                  <p class="text-sm text-muted-foreground">{{ displayContentType() }}</p>
                </div>
                <!-- File Hash -->
                @if (displayFileHash()) {
                  <div class="space-y-2">
                    <label hlmLabel>File Hash</label>
                    <p class="text-sm text-muted-foreground break-all">{{ displayFileHash() }}</p>
                  </div>
                }
              </div>
            </div>
          </div>
        </div>
      </ng-template>

      <ng-template #textContent>
        @if (displayExtractedText()) {
          <textarea
            hlmTextarea
            readonly
            class="h-full resize-none [field-sizing:fixed] bg-muted"
            [value]="displayExtractedText()"
          ></textarea>
        } @else {
          <div class="flex flex-col items-center justify-center gap-2 py-8 text-muted-foreground">
            <ng-icon hlm name="lucideFileSearch" size="xl" />
            <span class="text-sm">No extracted text available</span>
          </div>
        }
      </ng-template>

      @if (isMobile()) {
        <!-- Mobile: single tabbed layout -->
        <hlm-tabs tab="preview" class="min-w-0 flex flex-1 flex-col overflow-hidden rounded-md border">
          <hlm-tabs-list variant="line" class="mx-4 mt-3">
            <button hlmTabsTrigger="preview">Preview</button>
            <button hlmTabsTrigger="info">Info</button>
            <button hlmTabsTrigger="text">Text</button>
          </hlm-tabs-list>
          <div hlmTabsContent="preview" class="min-h-0 flex-1 overflow-hidden">
            <ng-container [ngTemplateOutlet]="previewContent" />
          </div>
          <div hlmTabsContent="info" class="relative min-h-0 flex-1">
            <div class="absolute inset-0 overflow-y-auto px-4 py-3">
              <ng-container [ngTemplateOutlet]="infoContent" />
            </div>
          </div>
          <div hlmTabsContent="text" class="min-h-0 min-w-0 flex-1 overflow-hidden px-4 py-3">
            <ng-container [ngTemplateOutlet]="textContent" />
          </div>
        </hlm-tabs>
      } @else {
        <!-- Desktop: resizable two-panel layout -->
        <div hlmResizableGroup direction="horizontal" class="min-h-0 flex-1 rounded-md border">
          <!-- Preview panel -->
          <div hlmResizablePanel [defaultSize]="75" [minSize]="50" class="overflow-hidden">
            <ng-container [ngTemplateOutlet]="previewContent" />
          </div>

          <hlm-resizable-handle withHandle />

          <!-- Info/Text tabs panel -->
          <div hlmResizablePanel [defaultSize]="25" [minSize]="20" class="flex flex-col overflow-hidden">
            <hlm-tabs tab="info" class="min-w-0 flex flex-1 flex-col overflow-hidden">
              <hlm-tabs-list variant="line" class="mx-4 mt-3">
                <button hlmTabsTrigger="info">Info</button>
                <button hlmTabsTrigger="text">Text</button>
              </hlm-tabs-list>
              <div hlmTabsContent="info" class="relative min-h-0 flex-1">
                <div class="absolute inset-0 overflow-y-auto px-4 py-3">
                  <ng-container [ngTemplateOutlet]="infoContent" />
                </div>
              </div>
              <div hlmTabsContent="text" class="min-h-0 min-w-0 flex-1 overflow-hidden px-4 py-3">
                <ng-container [ngTemplateOutlet]="textContent" />
              </div>
            </hlm-tabs>
          </div>
        </div>
      }
    }
  `,
  styles: [
    `
      .checkerboard {
        background-color: var(--background);
        background-image: repeating-conic-gradient(
          var(--muted) 0% 25%,
          transparent 0% 50%
        );
        background-size: 20px 20px;
      }
    `,
  ],
  host: {
    class: 'flex flex-col h-full',
  },
})
export class DocumentDetailPageComponent implements OnDestroy {
  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  editDocumentDate = signal('');
  editValidFrom = signal('');
  editValidUntil = signal('');
  editVersionLabel = signal('');
  editVersionNotes = signal('');
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  refreshTrigger = signal(0);

  formatBytes = formatBytes;

  private sidebarService = inject(HlmSidebarService);
  isMobile = this.sidebarService.isMobile;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private sanitizer = inject(DomSanitizer);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private pageHeaderService = inject(PageHeaderService);
  private hubSub: Subscription;

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  documentId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

  private versionId = signal<string | null>(this.route.snapshot.paramMap.get('versionId'));

  fileBlobUrl = signal<string | null>(null);

  safeFileUrl = computed(() => {
    const url = this.fileBlobUrl();
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });

  private hasDocumentChanges = computed(() => {
    const doc = this.document();
    if (!doc) return false;
    if (this.editName() !== doc.name) return true;
    const origTags = (doc.tags?.map((t) => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i]))
      return true;
    const origPartyIds = (doc.parties?.map((p) => p.id) ?? []).slice().sort();
    const currPartyIds = this.editParties().map((p) => p.id).slice().sort();
    if (
      origPartyIds.length !== currPartyIds.length ||
      origPartyIds.some((id, i) => id !== currPartyIds[i])
    )
      return true;
    if (this.editDocumentDate() !== this.toDateInputValue(doc.documentDate)) return true;
    if (this.editValidFrom() !== this.toDateInputValue(doc.validFrom)) return true;
    if (this.editValidUntil() !== this.toDateInputValue(doc.validUntil)) return true;
    return false;
  });

  private hasVersionChanges = computed(() => {
    const ver = this.activeVersion();
    if (!ver) return false;
    return this.editVersionLabel() !== (ver.label ?? '') ||
           this.editVersionNotes() !== (ver.notes ?? '');
  });

  hasChanges = computed(() => this.hasDocumentChanges() || this.hasVersionChanges());

  // Version computed signals
  sortedVersions = computed(() => {
    const doc = this.document();
    if (!doc?.versions) return [];
    return [...doc.versions].sort((a, b) => +b.versionNumber - +a.versionNumber);
  });

  private latestVersion = computed(() => {
    const versions = this.sortedVersions();
    return versions.length > 0 ? versions[0] : null;
  });

  activeVersion = computed(() => {
    const verId = this.versionId();
    const doc = this.document();
    if (!doc?.versions) return null;
    if (verId) {
      return doc.versions.find((r) => r.id === verId) ?? this.latestVersion();
    }
    return this.latestVersion();
  });

  hasMultipleVersions = computed(() => {
    const doc = this.document();
    return (doc?.versions?.length ?? 0) >= 2;
  });

  selectedVersionLabel = computed(() => {
    const ver = this.activeVersion();
    if (!ver) return 'Version';
    const latest = this.latestVersion();
    if (ver.id === latest?.id) {
      return ver.label ? `Latest - ${ver.label}` : 'Latest';
    }
    return this.versionLabel(ver);
  });

  // Version detail (for extractedText of non-latest versions)
  private versionDetail = signal<GetDocumentVersionResponse | null>(null);

  // Version-aware display signals
  displayContentType = computed(() => {
    const ver = this.activeVersion();
    return ver?.contentType ?? this.document()?.contentType ?? '';
  });

  displayFileSize = computed(() => {
    const ver = this.activeVersion();
    return ver?.fileSize ?? this.document()?.fileSize ?? 0;
  });

  displayFileExtension = computed(() => {
    const ver = this.activeVersion();
    return ver?.fileExtension ?? this.document()?.fileExtension ?? '';
  });

  displayFileHash = computed(() => {
    const verId = this.versionId();
    if (verId) {
      return this.versionDetail()?.fileHash ?? '';
    }
    return this.document()?.fileHash ?? '';
  });

  displayVersionCreatedAt = computed(() => {
    const ver = this.activeVersion();
    return ver?.createdAt ?? '';
  });

  displayExtractedText = computed(() => {
    const verId = this.versionId();
    if (verId) {
      // Viewing a specific version — use version detail's extractedText
      return this.versionDetail()?.extractedText ?? null;
    }
    // Viewing latest — use document-level extractedText
    return this.document()?.extractedText ?? null;
  });

  private fileSub: Subscription | null = null;
  private versionDetailSub: Subscription | null = null;

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
        this.editDocumentDate.set(this.toDateInputValue(doc.documentDate));
        this.editValidFrom.set(this.toDateInputValue(doc.validFrom));
        this.editValidUntil.set(this.toDateInputValue(doc.validUntil));
      }
    });

    effect(() => {
      const ver = this.activeVersion();
      if (ver) {
        this.editVersionLabel.set(ver.label ?? '');
        this.editVersionNotes.set(ver.notes ?? '');
      }
    });

    // Load file blob reactively — reads all signals atomically
    effect(() => {
      const id = this.documentId();
      const verId = this.versionId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger(); // track refresh trigger

      // untracked: revokeCurrentBlobUrl reads fileBlobUrl — must not become a dependency
      untracked(() => {
        this.fileSub?.unsubscribe();
        this.revokeCurrentBlobUrl();
      });

      if (!id) return;

      let url = `/api/v1/documents/${id}/file`;
      if (verId) {
        url += `?versionId=${verId}`;
      }

      this.fileSub = this.http
        .get(url, { responseType: 'blob' })
        .pipe(
          map((blob) => URL.createObjectURL(blob)),
          catchError(() => of(null)),
        )
        .subscribe((blobUrl) => this.fileBlobUrl.set(blobUrl));
    });

    // Load version detail when viewing a specific version
    effect(() => {
      const docId = this.documentId();
      const verId = this.versionId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger();

      this.versionDetailSub?.unsubscribe();

      if (!docId || !verId) {
        this.versionDetail.set(null);
        return;
      }

      this.versionDetailSub = getDocumentVersion(this.http, this.apiConfig.rootUrl, {
        documentId: docId,
        id: verId,
      })
        .pipe(
          map((r) => r.body as GetDocumentVersionResponse),
          catchError(() => of(null)),
        )
        .subscribe((detail) => this.versionDetail.set(detail));
    });

    afterNextRender(() => {
      const tpl = this.headerTitleRef();
      if (tpl) {
        this.pageHeaderService.setTitle(tpl);
      }
    });

    this.hubSub = this.processingHub.jobCompleted$.subscribe(() => {
      this.refreshTrigger.update((v) => v + 1);
    });
  }

  ngOnDestroy(): void {
    this.fileSub?.unsubscribe();
    this.versionDetailSub?.unsubscribe();
    this.revokeCurrentBlobUrl();
    this.hubSub.unsubscribe();
    this.pageHeaderService.clearTitle();
  }

  isPdf(): boolean {
    return this.displayContentType() === 'application/pdf';
  }

  isImage(): boolean {
    return this.displayContentType()?.startsWith('image/') ?? false;
  }

  versionLabel(ver: { versionNumber: number | string; label?: string | null }): string {
    const base = `v${ver.versionNumber}`;
    return ver.label ? `${base} - ${ver.label}` : base;
  }

  private location = inject(Location);

  navigateToLatestVersion(): void {
    const docId = this.documentId();
    if (!docId) return;
    this.versionId.set(null);
    this.location.go(`/documents/${docId}`);
  }

  onVersionSelected(versionId: string): void {
    const docId = this.documentId();
    if (!docId) return;
    const latest = this.latestVersion();
    const isLatest = latest && versionId === latest.id;
    this.versionId.set(isLatest ? null : versionId);

    const path = isLatest
      ? `/documents/${docId}`
      : `/documents/${docId}/versions/${versionId}`;
    this.location.go(path);
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

  toDateInputValue(isoString: string | null | undefined): string {
    return isoString ? isoString.substring(0, 10) : '';
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleDownload(): void {
    const url = this.fileBlobUrl();
    const doc = this.document();
    if (!url || !doc) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = `${doc.name}${this.displayFileExtension()}`;
    a.click();
  }

  handleDelete(): void {
    const id = this.documentId();
    if (!id) return;
    this.deleting.set(true);
    deleteDocument(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        toast.success('Document deleted.');
        this.router.navigate(['/documents']);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete document.');
      },
    });
  }

  handleSave(): void {
    const id = this.documentId();
    if (!id) return;
    const ver = this.activeVersion();
    this.saving.set(true);

    const docUpdate$ = this.hasDocumentChanges()
      ? updateDocument(this.http, this.apiConfig.rootUrl, {
          id,
          body: {
            name: this.editName(),
            documentDate: this.editDocumentDate() || null,
            validFrom: this.editValidFrom() || null,
            validUntil: this.editValidUntil() || null,
            tagPaths: this.editTags(),
            partyIds: this.editParties().map((p) => p.id),
          },
        })
      : of(null);

    const versionUpdate$ = this.hasVersionChanges() && ver
      ? updateDocumentVersion(this.http, this.apiConfig.rootUrl, {
          documentId: id,
          id: ver.id,
          body: {
            label: this.editVersionLabel() || null,
            notes: this.editVersionNotes() || null,
          },
        })
      : of(null);

    forkJoin([docUpdate$, versionUpdate$]).subscribe({
      next: () => {
        this.saving.set(false);
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
