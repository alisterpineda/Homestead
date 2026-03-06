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
  lucideMessageSquare,
  lucideHistory,
  lucideSend,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmScrollAreaImports } from '@/shared/components/spartan/scroll-area';
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { DatePickerComponent } from '@/shared/components/date-picker/date-picker.component';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { NgScrollbar } from 'ngx-scrollbar';
import { TagInputComponent } from '../../components/tag-input/tag-input.component';
import { PartyInputComponent } from '../../components/party-input/party-input.component';
import type { PartyOption } from '../../components/party-input/party-input.component';
import { AddVersionDialogComponent } from '../../components/add-version-dialog/add-version-dialog.component';
import { formatBytes, parseDate, toIsoDateString, datesEqual } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { getDocumentVersion } from '@/api/generated/fn/documents/get-document-version';
import { patchDocument } from '@/api/generated/fn/documents/patch-document';
import { patchDocumentVersion } from '@/api/generated/fn/documents/patch-document-version';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
import { getDocumentActivity } from '@/api/generated/fn/documents/get-document-activity';
import { createComment } from '@/api/generated/fn/documents/create-comment';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import { replace } from '@/shared/json-patch';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { GetDocumentVersionResponse } from '@/api/generated/models/get-document-version-response';
import type { GetActivityResponse } from '@/api/generated/models/get-activity-response';
import { AuthService } from '@/shared/services/auth.service';
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
    HlmAlertDialogImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
    HlmTextareaImports,
    HlmSeparatorImports,
    HlmScrollAreaImports,
    HlmInputGroupImports,
    NgScrollbar,
    AddVersionDialogComponent,
    HlmBadge,
    DatePickerComponent,
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
      lucideMessageSquare,
      lucideHistory,
      lucideSend,
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
        <button hlmBtn size="sm" variant="outline" (click)="handleDownload()">
          <ng-icon hlm name="lucideDownload" size="sm" />
          Download
        </button>
        <div class="ml-auto flex items-center gap-2">
          @if (auth.canWrite()) {
            <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="moreMenu">
              <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
            </button>
            <button hlmBtn size="sm" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
              <ng-icon hlm name="lucideSave" size="sm" />
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
            <ng-template #moreMenu>
              <hlm-dropdown-menu class="w-48">
                <button hlmDropdownMenuItem (triggered)="addVersionDialog.open()">
                  <ng-icon hlm name="lucideFilePlus2" size="sm" />
                  <span>Add Version</span>
                </button>
                <button hlmDropdownMenuItem variant="destructive" (triggered)="deleteDialogTrigger.click()" [disabled]="deleting()">
                  <ng-icon hlm name="lucideTrash2" size="sm" />
                  <span>Delete</span>
                </button>
              </hlm-dropdown-menu>
            </ng-template>

            <hlm-alert-dialog>
              <button hlmAlertDialogTrigger class="hidden" #deleteDialogTrigger></button>
              <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                <div hlmAlertDialogHeader>
                  <h2 hlmAlertDialogTitle>Delete document?</h2>
                  <p hlmAlertDialogDescription>
                    This action cannot be undone. This document and all its versions will be permanently deleted.
                  </p>
                </div>
                <div hlmAlertDialogFooter>
                  <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                  <button hlmAlertDialogAction variant="destructive" (click)="handleDelete(); ctx.close()" [disabled]="deleting()">
                    {{ deleting() ? 'Deleting...' : 'Delete' }}
                  </button>
                </div>
              </hlm-alert-dialog-content>
            </hlm-alert-dialog>
          }
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
          <div class="space-y-2">
            <label hlmLabel>Document Date</label>
            <app-date-picker [(date)]="editDocumentDate" ariaLabel="Document Date" />
          </div>

          <!-- Valid From / Valid Until -->
          <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div class="space-y-2">
              <label hlmLabel>Valid From</label>
              <app-date-picker [(date)]="editValidFrom" ariaLabel="Valid From" />
            </div>
            <div class="space-y-2">
              <label hlmLabel>Valid Until</label>
              <app-date-picker [(date)]="editValidUntil" ariaLabel="Valid Until" />
            </div>
          </div>

          <!-- Parties -->
          <div class="space-y-2">
            <label hlmLabel>Parties</label>
            <app-party-input [value]="editParties()" (valueChange)="editParties.set($event)" />
          </div>

          <!-- Tags -->
          <div class="space-y-2">
            <label hlmLabel>Tags</label>
            <app-tag-input [value]="editTags()" [initialColorMap]="tagColorMap()" (valueChange)="editTags.set($event)" />
          </div>

          <!-- Version Details -->
          <hlm-separator />

          <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">Version Details</p>

          <hlm-form-field>
            <label hlmLabel for="version-label">Label</label>
            <input hlmInput id="version-label" maxlength="256"
              placeholder="e.g., Final Draft, Signed Copy"
              [value]="editVersionLabel()"
              (input)="editVersionLabel.set(asInputValue($event))" />
          </hlm-form-field>
        </div>
      </ng-template>

      <ng-template #metadataContent>
        <div class="space-y-4">
          <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">Document</p>

          <!-- Added -->
          <div class="space-y-2">
            <label hlmLabel>Added</label>
            <p class="text-sm text-muted-foreground">
              {{ formatDate(doc.createdAt) }}
            </p>
          </div>

          <!-- Group -->
          @if (document()?.groupName; as groupName) {
            <div class="flex items-center gap-2">
              <span class="text-sm text-muted-foreground">Group:</span>
              <span hlmBadge variant="outline">{{ groupName }}</span>
            </div>
          }

          <!-- Version -->
          <hlm-separator />

          <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">Version</p>

          <!-- Version Added -->
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
      </ng-template>

      <ng-template #textContent>
        <textarea
          hlmTextarea
          aria-label="Extracted text"
          class="h-full resize-none [field-sizing:fixed]"
          placeholder="No extracted text available"
          [value]="editExtractedText()"
          (input)="editExtractedText.set(asInputValue($event))"
        ></textarea>
      </ng-template>

      <ng-template #activityContent>
        <div class="flex h-full flex-col">
          <ng-scrollbar hlm class="flex-1">
            <div class="px-4 py-3">
              @if (activityLoading()) {
                <div class="flex justify-center py-8">
                  <hlm-spinner />
                </div>
              } @else if (activityItems().length === 0) {
                <p class="text-center text-sm text-muted-foreground py-8">No activity yet.</p>
              } @else {
                @for (item of activityItems(); track item.id) {
                  @if (item.type === 'comment') {
                    <div class="flex gap-2.5 py-2">
                      <div class="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted">
                        <ng-icon hlm size="xs" name="lucideMessageSquare" />
                      </div>
                      <div class="min-w-0 flex-1">
                        <div class="flex items-baseline gap-2">
                          <span class="text-sm font-medium">{{ item.userDisplayName || item.userEmail || 'Someone' }}</span>
                          <span class="text-xs text-muted-foreground">{{ formatRelativeTime(item.timestamp) }}</span>
                        </div>
                        <p class="mt-0.5 text-sm whitespace-pre-wrap">{{ item.body }}</p>
                      </div>
                    </div>
                  } @else {
                    <div class="flex items-center gap-2.5 py-1.5">
                      <div class="flex size-6 shrink-0 items-center justify-center">
                        <ng-icon hlm size="xs" name="lucideHistory" class="text-muted-foreground" />
                      </div>
                      <p class="text-xs text-muted-foreground">
                        {{ item.summary }}
                        <span class="mx-0.5">&middot;</span>
                        {{ formatRelativeTime(item.timestamp) }}
                      </p>
                    </div>
                  }
                }
              }
            </div>
          </ng-scrollbar>

          <div class="border-t px-4 py-3">
            <label class="sr-only" for="activity-comment">Add a comment</label>
            <div hlmInputGroup>
              <input
                hlmInputGroupInput
                id="activity-comment"
                placeholder="Add a comment..."
                [value]="commentBody()"
                (input)="commentBody.set(asInputValue($event))"
                (keydown.enter)="submitComment()"
              />
              <hlm-input-group-addon align="inline-end">
                <button
                  hlmInputGroupButton
                  size="icon-xs"
                  [disabled]="!commentBody().trim() || commentSubmitting()"
                  (click)="submitComment()"
                  aria-label="Send comment"
                >
                  <ng-icon hlm name="lucideSend" size="sm" />
                </button>
              </hlm-input-group-addon>
            </div>
          </div>
        </div>
      </ng-template>

      @if (isMobile()) {
        <!-- Mobile: single tabbed layout -->
        <hlm-tabs tab="preview" class="min-w-0 flex flex-1 flex-col overflow-hidden">
          <hlm-tabs-list class="w-full" aria-label="Document tabs">
            <button hlmTabsTrigger="preview" class="flex-1">Preview</button>
            <button hlmTabsTrigger="info" class="flex-1">Info</button>
            <button hlmTabsTrigger="content" class="flex-1">Content</button>
            <button hlmTabsTrigger="metadata" class="flex-1">Metadata</button>
            <button hlmTabsTrigger="activity" class="flex-1">Activity</button>
          </hlm-tabs-list>
          <div hlmTabsContent="preview" class="min-h-0 flex-1 overflow-hidden rounded-md border">
            <ng-container [ngTemplateOutlet]="previewContent" />
          </div>
          <div hlmTabsContent="info" class="relative min-h-0 flex-1 rounded-md border">
            <div class="absolute inset-0 overflow-y-auto px-4 py-3">
              <ng-container [ngTemplateOutlet]="infoContent" />
            </div>
          </div>
          <div hlmTabsContent="content" class="min-h-0 min-w-0 flex-1 overflow-hidden rounded-md border px-4 py-3">
            <ng-container [ngTemplateOutlet]="textContent" />
          </div>
          <div hlmTabsContent="metadata" class="relative min-h-0 flex-1 rounded-md border">
            <div class="absolute inset-0 overflow-y-auto px-4 py-3">
              <ng-container [ngTemplateOutlet]="metadataContent" />
            </div>
          </div>
          <div hlmTabsContent="activity" class="relative min-h-0 flex-1 rounded-md border">
            <div class="absolute inset-0 flex flex-col">
              <ng-container [ngTemplateOutlet]="activityContent" />
            </div>
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
                <button hlmTabsTrigger="content">Content</button>
                <button hlmTabsTrigger="metadata">Metadata</button>
                <button hlmTabsTrigger="activity">Activity</button>
              </hlm-tabs-list>
              <div hlmTabsContent="info" class="relative min-h-0 flex-1">
                <div class="absolute inset-0 overflow-y-auto px-4 py-3">
                  <ng-container [ngTemplateOutlet]="infoContent" />
                </div>
              </div>
              <div hlmTabsContent="content" class="min-h-0 min-w-0 flex-1 overflow-hidden px-4 py-3">
                <ng-container [ngTemplateOutlet]="textContent" />
              </div>
              <div hlmTabsContent="metadata" class="relative min-h-0 flex-1">
                <div class="absolute inset-0 overflow-y-auto px-4 py-3">
                  <ng-container [ngTemplateOutlet]="metadataContent" />
                </div>
              </div>
              <div hlmTabsContent="activity" class="relative min-h-0 flex-1">
                <div class="absolute inset-0 flex flex-col">
                  <ng-container [ngTemplateOutlet]="activityContent" />
                </div>
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
  auth = inject(AuthService);
  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  editDocumentDate = signal<Date | null>(null);
  editValidFrom = signal<Date | null>(null);
  editValidUntil = signal<Date | null>(null);
  editVersionLabel = signal('');
  editExtractedText = signal('');
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  refreshTrigger = signal(0);
  activityItems = signal<GetActivityResponse[]>([]);
  activityLoading = signal(false);
  commentBody = signal('');
  commentSubmitting = signal(false);

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
    if (!datesEqual(this.editDocumentDate(), this.origDocumentDate())) return true;
    if (!datesEqual(this.editValidFrom(), this.origValidFrom())) return true;
    if (!datesEqual(this.editValidUntil(), this.origValidUntil())) return true;
    return false;
  });

  private hasVersionChanges = computed(() => {
    const ver = this.activeVersion();
    if (!ver) return false;
    if (this.editVersionLabel() !== (ver.label ?? '')) return true;
    const origText = this.displayExtractedText() ?? '';
    if (this.editExtractedText() !== origText) return true;
    return false;
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

  private origDocumentDate = computed(() => parseDate(this.document()?.documentDate));
  private origValidFrom = computed(() => parseDate(this.document()?.validFrom));
  private origValidUntil = computed(() => parseDate(this.document()?.validUntil));

  tagColorMap = computed(() => {
    const doc = this.document();
    if (!doc?.tags) return {};
    const map: Record<string, string> = {};
    for (const t of doc.tags) {
      if (t.color) map[t.path] = t.color;
    }
    return map;
  });

  constructor() {
    effect(() => {
      const doc = this.document();
      if (doc) {
        this.editName.set(doc.name);
        this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
        this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
        this.editDocumentDate.set(this.origDocumentDate());
        this.editValidFrom.set(this.origValidFrom());
        this.editValidUntil.set(this.origValidUntil());
      }
    });

    effect(() => {
      const ver = this.activeVersion();
      if (ver) {
        this.editVersionLabel.set(ver.label ?? '');
      }
    });

    effect(() => {
      const text = this.displayExtractedText();
      this.editExtractedText.set(text ?? '');
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

    // Load activity when document changes or refresh triggers
    effect(() => {
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.documentId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger();
      untracked(() => this.loadActivity());
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

    const docOps = this.buildDocumentPatchOps();
    const docUpdate$ = docOps.length > 0
      ? patchDocument(this.http, this.apiConfig.rootUrl, { id, body: docOps })
      : of(null);

    const verOps = this.buildVersionPatchOps();
    const versionUpdate$ = verOps.length > 0 && ver
      ? patchDocumentVersion(this.http, this.apiConfig.rootUrl, {
          documentId: id,
          id: ver.id,
          body: verOps,
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

  private buildDocumentPatchOps(): JsonPatchDocument {
    const doc = this.document();
    if (!doc) return [];
    const ops: JsonPatchDocument = [];

    if (this.editName() !== doc.name) {
      ops.push(replace('/name', this.editName()));
    }
    if (!datesEqual(this.editDocumentDate(), this.origDocumentDate())) {
      ops.push(replace('/documentDate', toIsoDateString(this.editDocumentDate())));
    }
    if (!datesEqual(this.editValidFrom(), this.origValidFrom())) {
      ops.push(replace('/validFrom', toIsoDateString(this.editValidFrom())));
    }
    if (!datesEqual(this.editValidUntil(), this.origValidUntil())) {
      ops.push(replace('/validUntil', toIsoDateString(this.editValidUntil())));
    }

    const origTags = (doc.tags?.map((t) => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i])) {
      ops.push(replace('/tagPaths', this.editTags()));
    }

    const origPartyIds = (doc.parties?.map((p) => p.id) ?? []).slice().sort();
    const currPartyIds = this.editParties().map((p) => p.id).slice().sort();
    if (origPartyIds.length !== currPartyIds.length || origPartyIds.some((pid, i) => pid !== currPartyIds[i])) {
      ops.push(replace('/partyIds', this.editParties().map((p) => p.id)));
    }

    return ops;
  }

  private buildVersionPatchOps(): JsonPatchDocument {
    const ver = this.activeVersion();
    if (!ver) return [];
    const ops: JsonPatchDocument = [];

    if (this.editVersionLabel() !== (ver.label ?? '')) {
      ops.push(replace('/label', this.editVersionLabel() || null));
    }
    const origText = this.displayExtractedText() ?? '';
    if (this.editExtractedText() !== origText) {
      ops.push(replace('/extractedText', this.editExtractedText() || null));
    }

    return ops;
  }

  private loadActivity(): void {
    const id = this.documentId();
    if (!id) return;
    this.activityLoading.set(true);
    getDocumentActivity(this.http, this.apiConfig.rootUrl, {
      documentId: id,
      Page: 1,
      PageSize: 100,
    })
      .pipe(
        map((r) => r.body.items ?? []),
        catchError(() => of([])),
      )
      .subscribe((items) => {
        this.activityItems.set(items);
        this.activityLoading.set(false);
      });
  }

  submitComment(): void {
    const id = this.documentId();
    const body = this.commentBody().trim();
    if (!id || !body) return;
    this.commentSubmitting.set(true);
    createComment(this.http, this.apiConfig.rootUrl, {
      documentId: id,
      body: { body },
    }).subscribe({
      next: () => {
        this.commentBody.set('');
        this.commentSubmitting.set(false);
        this.loadActivity();
      },
      error: () => {
        this.commentSubmitting.set(false);
        toast.error('Failed to post comment.');
      },
    });
  }

  formatRelativeTime(timestamp: string): string {
    const now = Date.now();
    const then = new Date(timestamp).getTime();
    const diffMs = now - then;
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return 'just now';
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    const diffDay = Math.floor(diffHr / 24);
    if (diffDay < 30) return `${diffDay}d ago`;
    return this.formatDate(timestamp);
  }
}
