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
  DestroyRef,
  TemplateRef,
} from '@angular/core';
import { Location, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, forkJoin, filter, finalize, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFileText,
  lucideFileQuestion,
  lucideDownload,
  lucideTrash2,
  lucideSave,
  lucideEllipsisVertical,
  lucideFilePlus2,
  lucideChevronDown,
  lucideChevronLeft,
  lucideChevronRight,
  lucideX,
  lucideCheck,
  lucideMessageSquare,
  lucideHistory,
  lucideSend,
  lucideRefreshCw,
  lucideArrowLeft,
  lucideSparkles,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroupImports } from '@/shared/components/spartan/button-group';
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
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmScrollAreaImports } from '@/shared/components/spartan/scroll-area';
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { DatePickerComponent } from '@/shared/components/date-picker/date-picker.component';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { NgScrollbar } from 'ngx-scrollbar';
import { HlmTooltipImports } from '@/shared/components/spartan/tooltip';
import { TagInputComponent } from '../tags/tag-input/tag-input.component';
import { PartyInputComponent } from '../parties/party-input/party-input.component';
import type { PartyOption } from '../parties/party-input/party-input.component';
import { AddVersionDialogComponent } from './add-version-dialog/add-version-dialog.component';
import { ReprocessDialogComponent } from './reprocess-dialog/reprocess-dialog.component';
import { ClassifyOptionsDialogComponent } from '../shared/classify-options-dialog/classify-options-dialog.component';
import { formatBytes, parseDate, toIsoDateString, datesEqual, triggerDownload } from '@/shared/utils';
import { DocumentApiService } from '@/api/services/document-api.service';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import type { JsonPatchDocument } from '@/api/generated/models/json-patch-document';
import { replace } from '@/shared/json-patch';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { GetDocumentVersionResponse } from '@/api/generated/models/get-document-version-response';
import type { GetActivityResponse } from '@/api/generated/models/get-activity-response';
import type { ClassifyPreviewResponse } from '@/api/generated/models/classify-preview-response';
import type { DocumentSuggestionField } from '@/api/generated/models/document-suggestion-field';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { OpenDocumentsService } from './open-documents.service';
import { DocumentEditStateService, type DocumentEditState } from './document-edit-state.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-document-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmButtonGroupImports,
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
    HlmTooltipImports,
    HlmSeparatorImports,
    HlmScrollAreaImports,
    HlmInputGroupImports,
    NgScrollbar,
    AddVersionDialogComponent,
    ReprocessDialogComponent,
    ClassifyOptionsDialogComponent,
    HlmBadge,
    HlmEmptyImports,
    DatePickerComponent,
    RouterLink,
  ],
  providers: [
    provideIcons({
      lucideFileText,
      lucideFileQuestion,
      lucideDownload,
      lucideTrash2,
      lucideSave,
      lucideEllipsisVertical,
      lucideFilePlus2,
      lucideChevronDown,
      lucideChevronLeft,
      lucideChevronRight,
      lucideX,
      lucideCheck,
      lucideMessageSquare,
      lucideHistory,
      lucideSend,
      lucideRefreshCw,
      lucideArrowLeft,
      lucideSparkles,
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

    @if (loading() && !document()) {
      <div class="flex h-full items-center justify-center">
        <hlm-spinner />
      </div>
    } @else if (notFound()) {
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucideFileQuestion" />
          </div>
          <div hlmEmptyTitle>Document not found</div>
          <div hlmEmptyDescription>This document may have been deleted or doesn't exist.</div>
        </div>
        <div hlmEmptyContent>
          <a hlmBtn variant="outline" routerLink="/documents">
            <ng-icon hlm name="lucideArrowLeft" size="sm" />
            Back to Documents
          </a>
        </div>
      </div>
    } @else if (document(); as doc) {
      <div class="flex items-center gap-2 mb-4">
        <div hlmButtonGroup aria-label="Document navigation">
          <button hlmBtn size="icon-sm" variant="outline" (click)="hasChanges() ? closeDialogTrigger.click() : handleClose()" aria-label="Close document">
            <ng-icon hlm name="lucideX" size="sm" />
          </button>
          <button hlmBtn size="icon-sm" variant="outline" (click)="navigatePrev()" [disabled]="!canNavigatePrev()" aria-label="Previous document">
            <ng-icon hlm name="lucideChevronLeft" size="sm" />
          </button>
          <button hlmBtn size="icon-sm" variant="outline" (click)="navigateNext()" [disabled]="!canNavigateNext()" aria-label="Next document">
            <ng-icon hlm name="lucideChevronRight" size="sm" />
          </button>
        </div>
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn size="sm" variant="outline" (click)="handleDownload()">
            <ng-icon hlm name="lucideDownload" size="sm" />
            Download
          </button>
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
                <button hlmDropdownMenuItem (triggered)="classifyOptionsDialog.open()" [disabled]="classifyingPreview()">
                  <ng-icon hlm name="lucideSparkles" size="sm" />
                  <span>{{ classifyingPreview() ? 'Classifying...' : 'Classify' }}</span>
                </button>
                <button hlmDropdownMenuItem (triggered)="summarizeOptionsDialog.open()" [disabled]="summarizing()">
                  <ng-icon hlm name="lucideFileText" size="sm" />
                  <span>{{ summarizing() ? 'Summarizing...' : 'Summarize' }}</span>
                </button>
                <button hlmDropdownMenuItem (triggered)="addVersionDialog.open()">
                  <ng-icon hlm name="lucideFilePlus2" size="sm" />
                  <span>Add Version</span>
                </button>
                <button hlmDropdownMenuItem (triggered)="reprocessDialog.open()">
                  <ng-icon hlm name="lucideRefreshCw" size="sm" />
                  <span>Reprocess Text</span>
                </button>
                @if (hasMultipleVersions()) {
                  <button hlmDropdownMenuItem variant="destructive" [hlmDropdownMenuTrigger]="deleteSubMenu" [disabled]="deleting() || deletingVersion()">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    <span>Delete</span>
                    <hlm-dropdown-menu-item-sub-indicator />
                  </button>
                  <ng-template #deleteSubMenu>
                    <hlm-dropdown-menu-sub>
                      <button hlmDropdownMenuItem variant="destructive" (triggered)="deleteVersionDialogTrigger.click()" [disabled]="deletingVersion()">
                        <ng-icon hlm name="lucideTrash2" size="sm" />
                        <span>This version</span>
                      </button>
                      <button hlmDropdownMenuItem variant="destructive" (triggered)="deleteDialogTrigger.click()" [disabled]="deleting()">
                        <ng-icon hlm name="lucideTrash2" size="sm" />
                        <span>Entire document</span>
                      </button>
                    </hlm-dropdown-menu-sub>
                  </ng-template>
                } @else {
                  <button hlmDropdownMenuItem variant="destructive" (triggered)="deleteDialogTrigger.click()" [disabled]="deleting()">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    <span>Delete</span>
                  </button>
                }
              </hlm-dropdown-menu>
            </ng-template>
          }
        </div>
      </div>

      <hlm-alert-dialog>
        <button hlmAlertDialogTrigger class="hidden" #closeDialogTrigger></button>
        <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
          <div hlmAlertDialogHeader>
            <h2 hlmAlertDialogTitle>Unsaved changes</h2>
            <p hlmAlertDialogDescription>
              You have unsaved changes that will be lost.
            </p>
          </div>
          <div hlmAlertDialogFooter>
            <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
            <button hlmAlertDialogAction variant="destructive" (click)="handleClose(); ctx.close()">
              Discard & Close
            </button>
          </div>
        </hlm-alert-dialog-content>
      </hlm-alert-dialog>

      <app-add-version-dialog
        #addVersionDialog
        [documentId]="doc.id"
        (versionCreated)="refreshTrigger.update(v => v + 1)"
      />

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

      <hlm-alert-dialog>
        <button hlmAlertDialogTrigger class="hidden" #deleteVersionDialogTrigger></button>
        <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
          <div hlmAlertDialogHeader>
            <h2 hlmAlertDialogTitle>Delete version?</h2>
            <p hlmAlertDialogDescription>
              This action cannot be undone. {{ selectedVersionLabel() }} of this document will be permanently deleted.
            </p>
          </div>
          <div hlmAlertDialogFooter>
            <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
            <button hlmAlertDialogAction variant="destructive" (click)="handleDeleteVersion(); ctx.close()" [disabled]="deletingVersion()">
              {{ deletingVersion() ? 'Deleting...' : 'Delete' }}
            </button>
          </div>
        </hlm-alert-dialog-content>
      </hlm-alert-dialog>

      <app-reprocess-dialog
        #reprocessDialog
        [documentId]="doc.id"
        [versionId]="activeVersion()!.id"
        (reprocessStarted)="onReprocessStarted($event)"
      />

      <app-classify-options-dialog
        #classifyOptionsDialog
        mode="single"
        [showFields]="true"
        (confirmed)="handleClassify($event)"
      />

      <app-classify-options-dialog
        #summarizeOptionsDialog
        mode="single"
        [showFields]="false"
        (confirmed)="handleSummarize($event)"
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
        } @else if (!fileBlobUrl() && !fileLoadError() && (isPdf() || isImage())) {
          <div class="flex h-full items-center justify-center">
            <hlm-spinner />
          </div>
        } @else {
          <div
            class="flex h-full flex-col items-center justify-center gap-2 bg-muted text-muted-foreground"
          >
            <ng-icon hlm name="lucideFileQuestion" size="xl" />
            <span class="text-sm">Preview not available</span>
          </div>
        }
      </ng-template>

      <ng-template #infoContent>
        <div class="space-y-4">
          <!-- AI Suggestions summary -->
          @if (totalSuggestionCount() > 0) {
            <div class="flex items-center gap-2 rounded-md border border-border bg-muted/50 px-3 py-2 text-sm">
              <ng-icon hlm name="lucideSparkles" size="sm" class="text-muted-foreground" />
              <span class="text-muted-foreground">AI suggestions ({{ totalSuggestionCount() }})</span>
              <span class="ml-auto flex items-center gap-1">
                @if (pendingSuggestionCount() >= 2) {
                  <button hlmBtn variant="ghost" size="sm" (click)="approveAllSuggestions()">Approve all</button>
                  <button hlmBtn variant="ghost" size="sm" (click)="declineAllSuggestions()">Decline all</button>
                }
                @if (hasTransientSuggestions()) {
                  <button hlmBtn variant="ghost" size="sm" (click)="applyAllTransient()">Apply all</button>
                  <button hlmBtn variant="ghost" size="sm" (click)="dismissAllTransient()">Dismiss all</button>
                }
              </span>
            </div>
          }

          <!-- Name -->
          <hlm-form-field>
            <label hlmLabel for="doc-name">
              Name
              @if (pendingNameSuggestion() || nameIsAiSuggested()) {
                <span hlmBadge class="ml-auto bg-ai/10 text-ai border-transparent dark:bg-ai/20">
                  <ng-icon hlm name="lucideSparkles" size="xs" />
                  AI Suggested
                </span>
              }
            </label>
            @if (pendingNameSuggestion(); as suggestion) {
              <hlm-input-group>
                <input
                  hlmInputGroupInput
                  id="doc-name"
                  [value]="editName()"
                  (input)="editName.set(asInputValue($event)); appliedTransientName.set(null)"
                  [hlmTooltip]="'Original: ' + (suggestion.originalValue ?? '(none)')"
                />
                <button hlmInputGroupButton size="icon-xs" (click)="approveSuggestion(suggestion.id)" aria-label="Approve name suggestion">
                  <ng-icon hlm name="lucideCheck" size="xs" />
                </button>
                <button hlmInputGroupButton size="icon-xs" (click)="declineSuggestion(suggestion.id)" aria-label="Decline name suggestion">
                  <ng-icon hlm name="lucideX" size="xs" />
                </button>
              </hlm-input-group>
            } @else {
              <input
                hlmInput
                id="doc-name"
                [value]="editName()"
                (input)="editName.set(asInputValue($event)); appliedTransientName.set(null)"
              />
            }
          </hlm-form-field>

          <!-- Parties -->
          <div class="space-y-2">
            <label hlmLabel>Parties</label>
            <app-party-input
              [value]="editParties()"
              [aiPendingParties]="aiPendingPartyIds()"
              [aiTransientParties]="aiTransientPartyIds()"
              (valueChange)="onEditPartiesChanged($event)"
              (partyApproved)="approvePartyById($event)"
              (partyDeclined)="declinePartyById($event)"
            />
          </div>

          <!-- Tags -->
          <div class="space-y-2">
            <label hlmLabel>Tags</label>
            <app-tag-input
              [value]="editTags()"
              [initialColorMap]="tagColorMap()"
              [aiPendingTags]="aiPendingTagPaths()"
              [aiTransientTags]="aiTransientTagPaths()"
              (valueChange)="onEditTagsChanged($event)"
              (tagApproved)="approveTagByPath($event)"
              (tagDeclined)="declineTagByPath($event)"
            />
          </div>

          <!-- Document Date -->
          <div class="space-y-2">
            <label hlmLabel>Document Date</label>
            <app-date-picker [(date)]="editDocumentDate" ariaLabel="Document Date" />
          </div>

          <!-- Expiry Date -->
          <div class="space-y-2">
            <label hlmLabel>Expiry Date</label>
            <app-date-picker [(date)]="editExpiryDate" ariaLabel="Expiry Date" />
          </div>

          <!-- Notes -->
          <div class="space-y-2">
            <label hlmLabel for="doc-notes">Notes</label>
            <textarea
              hlmTextarea
              id="doc-notes"
              rows="3"
              placeholder="Add notes about this document..."
              [value]="editNotes()"
              (input)="editNotes.set(asInputValue($event))"
            ></textarea>
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

          <!-- Modified -->
          <div class="space-y-2">
            <label hlmLabel>Modified</label>
            <p class="text-sm text-muted-foreground">
              {{ formatDate(doc.updatedAt) }}
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
          <!-- Version Modified -->
          @if (displayVersionUpdatedAt()) {
            <div class="space-y-2">
              <label hlmLabel>Modified</label>
              <p class="text-sm text-muted-foreground">
                {{ formatDate(displayVersionUpdatedAt()) }}
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
        <div class="flex h-full flex-col gap-3">
          @if (document()?.summary) {
            <div class="space-y-1.5">
              <label hlmLabel>Summary</label>
              <p class="text-sm text-muted-foreground">{{ document()!.summary }}</p>
            </div>
          }
          <div class="flex min-h-0 flex-1 flex-col gap-1.5">
            <label hlmLabel for="extracted-text">Extracted Text</label>
            <textarea
              hlmTextarea
              id="extracted-text"
              class="min-h-0 flex-1 resize-none [field-sizing:fixed]"
              placeholder="No extracted text available"
              [value]="editExtractedText()"
              (input)="editExtractedText.set(asInputValue($event))"
            ></textarea>
          </div>
        </div>
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
  editExpiryDate = signal<Date | null>(null);
  editNotes = signal('');
  editVersionLabel = signal('');
  editExtractedText = signal('');
  saving = signal(false);
  deleting = signal(false);
  deletingVersion = signal(false);
  loading = signal(false);
  notFound = signal(false);
  refreshTrigger = signal(0);
  activityItems = signal<GetActivityResponse[]>([]);
  activityLoading = signal(false);
  commentBody = signal('');
  commentSubmitting = signal(false);
  transientClassification = signal<ClassifyPreviewResponse | null>(null);
  classifyingPreview = signal(false);
  summarizing = signal(false);
  appliedTransientName = signal<string | null>(null);
  preClassifyName = signal<string | null>(null);

  formatBytes = formatBytes;

  private sidebarService = inject(HlmSidebarService);
  private openDocumentsService = inject(OpenDocumentsService);
  private editStateService = inject(DocumentEditStateService);
  private destroyRef = inject(DestroyRef);
  isMobile = this.sidebarService.isMobile;

  private documentApi = inject(DocumentApiService);
  private versionApi = inject(DocumentVersionApiService);
  private sanitizer = inject(DomSanitizer);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private errorHandler = inject(ErrorHandlerService);
  private pageHeaderService = inject(PageHeaderService);
  private hubSub: Subscription;
  private initializedDocId: string | null = null;
  private initializedVersionId: string | null = null;

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  documentId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

  canNavigatePrev = computed(() => {
    const id = this.documentId();
    return id ? this.openDocumentsService.getPrevDocumentId(id) !== null : false;
  });

  canNavigateNext = computed(() => {
    const id = this.documentId();
    return id ? this.openDocumentsService.getNextDocumentId(id) !== null : false;
  });

  private versionId = signal<string | null>(this.route.snapshot.paramMap.get('versionId'));

  fileBlobUrl = signal<string | null>(null);
  fileLoadError = signal(false);

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
    if (!datesEqual(this.editExpiryDate(), this.origExpiryDate())) return true;
    if (this.editNotes() !== this.origNotes()) return true;
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

  displayVersionUpdatedAt = computed(() => {
    const ver = this.activeVersion();
    return ver?.updatedAt ?? '';
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
  private classifySub: Subscription | null = null;
  private summarizeSub: Subscription | null = null;
  private preserveTransientOnNextInit = false;

  private documentData = toSignal(
    combineLatest([toObservable(this.documentId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        return this.documentApi.getDocument({ id }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as GetDocumentResponse;
          }),
          catchError((err) => {
            this.loading.set(false);
            if (err instanceof HttpErrorResponse && err.status === 404) {
              this.notFound.set(true);
            } else if (!this.errorHandler.isServerOrNetworkError(err)) {
              this.errorHandler.handleError(err, 'Failed to load document.');
            }
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  document = this.documentData;

  private origDocumentDate = computed(() => parseDate(this.document()?.documentDate));
  private origExpiryDate = computed(() => parseDate(this.document()?.expiryDate));
  private origNotes = computed(() => this.document()?.notes ?? '');

  tagColorMap = computed(() => {
    const doc = this.document();
    const map: Record<string, string> = {};
    if (doc?.tags) {
      for (const t of doc.tags) {
        const c = t.effectiveColor ?? t.color;
        if (c) map[t.path] = c;
      }
    }
    const tc = this.transientClassification();
    if (tc?.tags) {
      for (const t of tc.tags) {
        const c = t.effectiveColor ?? t.color;
        if (c) map[t.path] = c;
      }
    }
    return map;
  });

  // AI suggestion computed signals
  pendingSuggestions = computed(() =>
    (this.document()?.suggestions ?? []).filter(s => s.status === 'Pending'),
  );
  pendingNameSuggestion = computed(() =>
    this.pendingSuggestions().find(s => s.field === 'Name'),
  );
  pendingTagSuggestions = computed(() => {
    const suggestions = this.pendingSuggestions().filter(s => s.field === 'Tags');
    const tags = this.document()?.tags ?? [];
    const tagById = new Map(tags.map(t => [t.id, t]));
    return suggestions
      .map(s => ({ ...s, tag: tagById.get(s.suggestedValue) }))
      .filter(s => s.tag != null);
  });
  pendingPartySuggestions = computed(() => {
    const suggestions = this.pendingSuggestions().filter(s => s.field === 'Parties');
    const parties = this.document()?.parties ?? [];
    const partyById = new Map(parties.map(p => [p.id, p]));
    return suggestions
      .map(s => ({ ...s, party: partyById.get(s.suggestedValue) }))
      .filter(s => s.party != null);
  });
  pendingSuggestionCount = computed(() => this.pendingSuggestions().length);

  aiPendingTagPaths = computed(() => {
    const suggestions = this.pendingTagSuggestions();
    return new Set(suggestions.map(s => s.tag!.path));
  });

  aiPendingPartyIds = computed(() => {
    const suggestions = this.pendingPartySuggestions();
    return new Set(suggestions.map(s => s.party!.id));
  });

  aiTransientTagPaths = signal<Set<string>>(new Set());
  aiTransientPartyIds = signal<Set<string>>(new Set());

  pendingTagPathToSuggestionId = computed(() => {
    const suggestions = this.pendingTagSuggestions();
    const map = new Map<string, string>();
    for (const s of suggestions) {
      map.set(s.tag!.path, s.id);
    }
    return map;
  });

  pendingPartyIdToSuggestionId = computed(() => {
    const suggestions = this.pendingPartySuggestions();
    const map = new Map<string, string>();
    for (const s of suggestions) {
      map.set(s.party!.id, s.id);
    }
    return map;
  });

  hasTransientSuggestions = computed(() =>
    this.aiTransientTagPaths().size > 0 || this.aiTransientPartyIds().size > 0 || this.nameIsAiSuggested(),
  );

  nameIsAiSuggested = computed(() => {
    const applied = this.appliedTransientName();
    return applied !== null && this.editName() === applied;
  });

  totalSuggestionCount = computed(() =>
    this.pendingSuggestionCount()
    + this.aiTransientTagPaths().size
    + this.aiTransientPartyIds().size
    + (this.nameIsAiSuggested() ? 1 : 0),
  );

  constructor() {
    // Save edit state when documentId changes (component reuse on sidebar navigation)
    // and initialize edit fields when a new document loads.
    let prevDocId: string | null = null;
    effect(() => {
      const id = this.documentId();
      if (prevDocId && prevDocId !== id) {
        if (untracked(() => this.hasChanges())) {
          untracked(() => this.editStateService.saveState(prevDocId!, this.buildEditState()));
        }
        untracked(() => this.editStateService.setActiveHasChanges(prevDocId!, false));
        untracked(() => this.classifySub?.unsubscribe());
      }
      prevDocId = id;
    });

    effect(() => {
      const doc = this.document();
      if (doc) {
        // Only initialize edit fields when switching to a different document.
        // Re-fetches of the same document (e.g., from refreshTrigger after a
        // background job completes) should not overwrite the user's pending edits.
        if (doc.id === this.initializedDocId) return;
        this.initializedDocId = doc.id;

        let preservedTransient = false;
        if (this.preserveTransientOnNextInit) {
          this.preserveTransientOnNextInit = false;
          preservedTransient = true;
        } else {
          this.clearTransientState();
        }

        // Capture transient party options (with names) before editParties is reset/restored,
        // so the preserve block below can re-add them without depending on the non-persisted
        // transientClassification signal.
        const priorTransientPartyOptions = preservedTransient
          ? untracked(() => this.editParties().filter(p => this.aiTransientPartyIds().has(p.id)))
          : [];

        const saved = untracked(() => this.editStateService.getState(doc.id));
        if (saved) {
          this.editName.set(saved.editName);
          this.editTags.set(saved.editTags);
          this.editParties.set(saved.editParties);
          this.editDocumentDate.set(saved.editDocumentDate ? parseDate(saved.editDocumentDate) : null);
          this.editExpiryDate.set(saved.editExpiryDate ? parseDate(saved.editExpiryDate) : null);
          this.editNotes.set(saved.editNotes ?? '');
          this.editVersionLabel.set(saved.editVersionLabel);
          this.editExtractedText.set(saved.editExtractedText);
          if (saved.transientAi) {
            this.appliedTransientName.set(saved.transientAi.appliedTransientName);
            this.preClassifyName.set(saved.transientAi.preClassifyName);
            this.aiTransientTagPaths.set(new Set(saved.transientAi.aiTransientTagPaths));
            this.aiTransientPartyIds.set(new Set(saved.transientAi.aiTransientPartyIds));
          }
          this.initializedVersionId = saved.versionId
            ?? untracked(() => this.activeVersion()?.id ?? null);
          untracked(() => this.editStateService.clearState(doc.id));
        } else {
          this.editName.set(doc.name);
          this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
          this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
          this.editDocumentDate.set(this.origDocumentDate());
          this.editExpiryDate.set(this.origExpiryDate());
          this.editNotes.set(this.origNotes());
          this.initializedVersionId = null;
        }

        if (preservedTransient) {
          const transient = this.aiTransientTagPaths();
          if (transient.size > 0) {
            const serverPaths = new Set(this.editTags());
            const stillTransient = [...transient].filter(p => !serverPaths.has(p));
            if (stillTransient.length > 0) {
              this.editTags.update(tags => [...tags, ...stillTransient]);
            }
            if (stillTransient.length < transient.size) {
              this.aiTransientTagPaths.set(new Set(stillTransient));
            }
          }

          const transientPartyIds = this.aiTransientPartyIds();
          if (transientPartyIds.size > 0) {
            const serverPartyIds = new Set((doc.parties ?? []).map(p => p.id));
            const stillTransientIds = new Set(
              [...transientPartyIds].filter(id => !serverPartyIds.has(id)),
            );
            const currentPartyIds = new Set(this.editParties().map(p => p.id));
            const toAdd = priorTransientPartyOptions
              .filter(p => stillTransientIds.has(p.id) && !currentPartyIds.has(p.id));
            if (toAdd.length > 0) {
              this.editParties.update(parties => [
                ...parties,
                ...toAdd.map(p => ({ id: p.id, name: p.name })),
              ]);
            }
            if (stillTransientIds.size !== transientPartyIds.size) {
              this.aiTransientPartyIds.set(stillTransientIds);
            }
          }
        }
      }
    });

    // Register document as "open" in sidebar
    effect(() => {
      const doc = this.document();
      if (doc) {
        untracked(() => this.openDocumentsService.addDocument(doc.id, doc.name));
      }
    });

    // Initialize version-level edit fields. Guard prevents refreshTrigger-driven
    // re-emits from overwriting pending user edits.
    effect(() => {
      const ver = this.activeVersion();
      const text = this.displayExtractedText();
      if (!ver) return;
      if (ver.id === this.initializedVersionId) return;
      const verId = untracked(() => this.versionId());
      if (verId && text === null) return; // wait for versionDetail on non-latest
      this.initializedVersionId = ver.id;
      this.editVersionLabel.set(ver.label ?? '');
      this.editExtractedText.set(text ?? '');
    });

    // Track active changes for sidebar indicator
    effect(() => {
      const id = this.documentId();
      const doc = this.document();
      const changed = this.hasChanges();
      // Only track when the loaded document matches the current documentId
      // to avoid false positives during document transitions
      if (id && doc?.id === id) {
        untracked(() => this.editStateService.setActiveHasChanges(id, changed));
      }
    });

    // Load file blob reactively — reads all signals atomically
    effect(() => {
      const id = this.documentId();
      const verId = this.versionId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger(); // track refresh trigger (reprocessing, version upload)

      this.fileSub?.unsubscribe();

      if (!id) return;

      untracked(() => this.fileLoadError.set(false));

      this.fileSub = this.documentApi
        .getDocumentFile(id, verId)
        .pipe(
          map((blob) => URL.createObjectURL(blob)),
          catchError((err) => {
            this.fileLoadError.set(true);
            if (!this.errorHandler.isServerOrNetworkError(err)) {
              this.errorHandler.handleError(err, 'Failed to load file.');
            }
            return of(null);
          }),
        )
        .subscribe((blobUrl) => this.swapBlobUrl(blobUrl));
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

      this.versionDetailSub = this.versionApi.getDocumentVersion({
        documentId: docId,
        id: verId,
      })
        .pipe(
          map((r) => r as GetDocumentVersionResponse),
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

      const beforeUnloadHandler = (e: BeforeUnloadEvent) => {
        if (this.hasChanges()) {
          const id = this.documentId();
          if (id) this.editStateService.saveState(id, this.buildEditState());
          e.preventDefault();
        }
      };
      window.addEventListener('beforeunload', beforeUnloadHandler);
      this.destroyRef.onDestroy(() => window.removeEventListener('beforeunload', beforeUnloadHandler));
    });

    // Re-register header title after in-component navigations (NavigationStart clears it)
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    ).subscribe(() => {
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
    const id = this.documentId();
    if (id && this.openDocumentsService.openDocuments().some(d => d.id === id) && this.hasChanges()) {
      this.editStateService.saveState(id, this.buildEditState());
    }
    if (id) {
      this.editStateService.setActiveHasChanges(id, false);
    }
    this.fileSub?.unsubscribe();
    this.versionDetailSub?.unsubscribe();
    this.classifySub?.unsubscribe();
    this.summarizeSub?.unsubscribe();
    this.swapBlobUrl(null);
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

  private swapBlobUrl(newUrl: string | null): void {
    const oldUrl = this.fileBlobUrl();
    this.fileBlobUrl.set(newUrl);
    if (oldUrl) URL.revokeObjectURL(oldUrl);
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
    triggerDownload(url, `${doc.name}${this.displayFileExtension()}`);
  }

  handleDelete(): void {
    const id = this.documentId();
    if (!id) return;
    this.deleting.set(true);
    this.documentApi.deleteDocument({ id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete document.'),
    ).subscribe({
      next: () => {
        this.editStateService.discardChanges(id);
        this.openDocumentsService.removeDocument(id);
        toast.success('Document deleted.');
        this.router.navigate(['/documents']);
      },
    });
  }

  handleDeleteVersion(): void {
    const docId = this.documentId();
    const ver = this.activeVersion();
    if (!docId || !ver) return;
    this.deletingVersion.set(true);
    this.versionApi.deleteDocumentVersion({ documentId: docId, id: ver.id }).pipe(
      handleMutationError(this.errorHandler, this.deletingVersion, 'Failed to delete version.'),
    ).subscribe({
      next: () => {
        this.deletingVersion.set(false);
        toast.success('Version deleted.');
        // The active version is gone; reset version-edit state and show the new latest.
        this.initializedVersionId = null;
        this.navigateToLatestVersion();
        this.refreshTrigger.update(v => v + 1);
      },
    });
  }

  handleClose(): void {
    const id = this.documentId();
    if (id) {
      this.editStateService.discardChanges(id);
      this.openDocumentsService.removeDocument(id);
    }
    this.router.navigate(['/documents']);
  }

  navigatePrev(): void {
    this.navigateToSibling((id) => this.openDocumentsService.getPrevDocumentId(id));
  }

  navigateNext(): void {
    this.navigateToSibling((id) => this.openDocumentsService.getNextDocumentId(id));
  }

  private navigateToSibling(getSiblingId: (id: string) => string | null): void {
    const id = this.documentId();
    if (!id) return;
    const siblingId = getSiblingId(id);
    if (!siblingId) return;
    if (this.hasChanges()) {
      this.editStateService.saveState(id, this.buildEditState());
    }
    this.router.navigate(['/documents', siblingId]);
  }

  onReprocessStarted(jobId: string): void {
    toast.promise(this.processingHub.waitForJob(jobId), {
      loading: 'Reprocessing text extraction...',
      success: 'Text extraction completed successfully.',
      error: (err: unknown) => `Reprocessing failed: ${err instanceof Error ? err.message : 'An unknown error occurred.'}`,
    });
  }

  handleClassify(options: { aiProviderId: string | null; fields: DocumentSuggestionField[] }): void {
    const id = this.documentId();
    if (!id) return;
    this.classifyingPreview.set(true);
    this.transientClassification.set(null);
    this.appliedTransientName.set(null);
    this.preClassifyName.set(this.preClassifyName() ?? this.editName());
    this.classifySub?.unsubscribe();

    // Bridge the (synchronous) classify request to a promise so a single toast
    // tracks loading -> done, instead of dismissing the loading toast and
    // flashing a separate success toast. finalize() settles the promise on
    // error/unsubscribe too (handleMutationError swallows 4xx into a silent
    // completion); rejecting after a resolve is a no-op. Omitting `error` from
    // toast.promise means a rejection just dismisses the loading toast and
    // leaves error toasting to handleMutationError / the interceptor.
    let settle!: { resolve: (count: number) => void; reject: () => void };
    const classified = new Promise<number>((resolve, reject) => {
      settle = { resolve, reject };
    });

    this.classifySub = this.documentApi.classifyPreview({
      documentId: id,
      body: { aiProviderId: options.aiProviderId, fields: options.fields },
    }).pipe(
      handleMutationError(this.errorHandler, this.classifyingPreview, 'Failed to classify document.'),
      finalize(() => settle.reject()),
    ).subscribe({
      next: (result) => {
        this.classifyingPreview.set(false);
        this.transientClassification.set(result);
        if (result.suggestedTitle && this.editName() === this.preClassifyName()) {
          this.editName.set(result.suggestedTitle);
          this.appliedTransientName.set(result.suggestedTitle);
        }
        let newTagCount = 0;
        if (result.tags?.length) {
          const currentPaths = new Set(this.editTags());
          const newPaths = result.tags.filter(t => !currentPaths.has(t.path)).map(t => t.path);
          newTagCount = newPaths.length;
          if (newPaths.length > 0) {
            this.editTags.update(tags => [...tags, ...newPaths]);
            const existing = this.aiTransientTagPaths();
            this.aiTransientTagPaths.set(new Set([...existing, ...newPaths]));
          }
        }
        let newPartyCount = 0;
        if (result.parties?.length) {
          const currentIds = new Set(this.editParties().map(p => p.id));
          const newParties = result.parties.filter(p => !currentIds.has(p.id));
          newPartyCount = newParties.length;
          if (newParties.length > 0) {
            this.editParties.update(parties => [
              ...parties,
              ...newParties.map(p => ({ id: p.id, name: p.name })),
            ]);
            const existing = this.aiTransientPartyIds();
            this.aiTransientPartyIds.set(new Set([...existing, ...newParties.map(p => p.id)]));
          }
        }
        settle.resolve((result.suggestedTitle ? 1 : 0) + newTagCount + newPartyCount);
      },
    });

    toast.promise(classified, {
      loading: 'Classifying document...',
      success: (count) =>
        count > 0
          ? `${count} suggestion${count === 1 ? '' : 's'} ready to review.`
          : 'No new suggestions.',
    });
  }

  handleSummarize(options: { aiProviderId: string | null }): void {
    const id = this.documentId();
    if (!id) return;
    this.summarizing.set(true);
    this.summarizeSub?.unsubscribe();
    const toastId = toast.loading('Summarizing document...');
    this.summarizeSub = this.documentApi.summarizeDocument({
      documentId: id,
      body: { aiProviderId: options.aiProviderId },
    }).pipe(
      handleMutationError(this.errorHandler, this.summarizing, 'Failed to summarize document.'),
      finalize(() => toast.dismiss(toastId)),
    ).subscribe({
      next: (result) => {
        this.summarizing.set(false);
        // Refresh the document so the read-only summary block reflects the new summary.
        this.refreshTrigger.update(v => v + 1);
        toast.success(result.summary ? 'Summary updated.' : 'No summary was generated.');
      },
    });
  }

  applyAllTransient(): void {
    this.clearTransientState();
  }

  dismissAllTransient(): void {
    const transientPaths = this.aiTransientTagPaths();
    if (transientPaths.size > 0) {
      this.editTags.update(tags => tags.filter(t => !transientPaths.has(t)));
    }
    const transientPartyIds = this.aiTransientPartyIds();
    if (transientPartyIds.size > 0) {
      this.editParties.update(parties => parties.filter(p => !transientPartyIds.has(p.id)));
    }
    const pre = this.preClassifyName();
    if (pre !== null) {
      this.editName.set(pre);
    }
    this.clearTransientState();
  }

  approveTagByPath(path: string): void {
    const suggestionId = this.pendingTagPathToSuggestionId().get(path);
    if (suggestionId) {
      this.approveSuggestion(suggestionId);
    } else {
      toast.error('Could not find suggestion to approve.');
    }
  }

  declineTagByPath(path: string): void {
    const suggestionId = this.pendingTagPathToSuggestionId().get(path);
    if (suggestionId) {
      this.declineSuggestion(suggestionId);
    } else {
      toast.error('Could not find suggestion to decline.');
    }
  }

  onEditTagsChanged(newTags: string[]): void {
    const newSet = new Set(newTags);
    const removed = new Set(this.editTags().filter(t => !newSet.has(t)));
    this.editTags.set(newTags);
    if (removed.size > 0) {
      const transient = this.aiTransientTagPaths();
      const updated = new Set([...transient].filter(t => !removed.has(t)));
      if (updated.size !== transient.size) {
        this.aiTransientTagPaths.set(updated);
      }
    }
  }

  approvePartyById(partyId: string): void {
    const suggestionId = this.pendingPartyIdToSuggestionId().get(partyId);
    if (suggestionId) {
      this.approveSuggestion(suggestionId);
    } else {
      toast.error('Could not find suggestion to approve.');
    }
  }

  declinePartyById(partyId: string): void {
    const suggestionId = this.pendingPartyIdToSuggestionId().get(partyId);
    if (suggestionId) {
      this.declineSuggestion(suggestionId);
    } else {
      toast.error('Could not find suggestion to decline.');
    }
  }

  onEditPartiesChanged(newParties: PartyOption[]): void {
    const newIds = new Set(newParties.map(p => p.id));
    const removed = new Set(this.editParties().filter(p => !newIds.has(p.id)).map(p => p.id));
    this.editParties.set(newParties);
    if (removed.size > 0) {
      const transient = this.aiTransientPartyIds();
      const updated = new Set([...transient].filter(id => !removed.has(id)));
      if (updated.size !== transient.size) {
        this.aiTransientPartyIds.set(updated);
      }
    }
  }

  approveSuggestion(suggestionId: string): void {
    const docId = this.documentId();
    if (!docId) return;
    this.documentApi.approveSuggestion({ documentId: docId, suggestionId }).pipe(
      handleMutationError(this.errorHandler, signal(false), 'Failed to approve suggestion.'),
    ).subscribe({
      next: () => {
        this.preserveTransientOnNextInit = true;
        this.initializedDocId = null;
        this.initializedVersionId = null;
        this.refreshTrigger.update(v => v + 1);
        toast.success('Suggestion approved.');
      },
    });
  }

  declineSuggestion(suggestionId: string): void {
    const docId = this.documentId();
    if (!docId) return;
    this.documentApi.declineSuggestion({ documentId: docId, suggestionId }).pipe(
      handleMutationError(this.errorHandler, signal(false), 'Failed to decline suggestion.'),
    ).subscribe({
      next: () => {
        this.preserveTransientOnNextInit = true;
        this.initializedDocId = null;
        this.initializedVersionId = null;
        this.refreshTrigger.update(v => v + 1);
        toast.success('Suggestion declined.');
      },
    });
  }

  approveAllSuggestions(): void {
    const docId = this.documentId();
    if (!docId) return;
    this.documentApi.approveAllSuggestions({ documentId: docId }).pipe(
      handleMutationError(this.errorHandler, signal(false), 'Failed to approve suggestions.'),
    ).subscribe({
      next: () => {
        this.preserveTransientOnNextInit = true;
        this.initializedDocId = null;
        this.initializedVersionId = null;
        this.refreshTrigger.update(v => v + 1);
        toast.success('All suggestions approved.');
      },
    });
  }

  declineAllSuggestions(): void {
    const docId = this.documentId();
    if (!docId) return;
    this.documentApi.declineAllSuggestions({ documentId: docId }).pipe(
      handleMutationError(this.errorHandler, signal(false), 'Failed to decline suggestions.'),
    ).subscribe({
      next: () => {
        this.preserveTransientOnNextInit = true;
        this.initializedDocId = null;
        this.initializedVersionId = null;
        this.refreshTrigger.update(v => v + 1);
        toast.success('All suggestions declined.');
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
      ? this.documentApi.patchDocument({ id, body: docOps })
      : of(null);

    const verOps = this.buildVersionPatchOps();
    const versionUpdate$ = verOps.length > 0 && ver
      ? this.versionApi.patchDocumentVersion({
          documentId: id,
          id: ver.id,
          body: verOps,
        })
      : of(null);

    forkJoin([docUpdate$, versionUpdate$]).pipe(
      handleMutationError(this.errorHandler, this.saving, 'Failed to save changes.'),
    ).subscribe({
      next: () => {
        this.saving.set(false);
        this.clearTransientState();
        const savedId = this.documentId();
        if (savedId) this.editStateService.clearState(savedId);
        // Reset so the re-fetch reinitializes edit fields with server values
        this.initializedDocId = null;
        this.initializedVersionId = null;
        this.refreshTrigger.update((v) => v + 1);
        toast.success('Document updated.');
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
    if (!datesEqual(this.editExpiryDate(), this.origExpiryDate())) {
      ops.push(replace('/expiryDate', toIsoDateString(this.editExpiryDate())));
    }
    if (this.editNotes() !== this.origNotes()) {
      ops.push(replace('/notes', this.editNotes()));
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

  private clearTransientState(): void {
    this.transientClassification.set(null);
    this.appliedTransientName.set(null);
    this.preClassifyName.set(null);
    this.aiTransientTagPaths.set(new Set());
    this.aiTransientPartyIds.set(new Set());
  }

  private buildEditState(): DocumentEditState {
    const appliedName = this.appliedTransientName();
    const preName = this.preClassifyName();
    const transientPaths = this.aiTransientTagPaths();
    const transientPartyIds = this.aiTransientPartyIds();
    const hasTransient = appliedName !== null || preName !== null
      || transientPaths.size > 0 || transientPartyIds.size > 0;

    return {
      editName: this.editName(),
      editDocumentDate: toIsoDateString(this.editDocumentDate()),
      editExpiryDate: toIsoDateString(this.editExpiryDate()),
      editNotes: this.editNotes(),
      editParties: this.editParties(),
      editTags: this.editTags(),
      editVersionLabel: this.editVersionLabel(),
      editExtractedText: this.editExtractedText(),
      versionId: this.versionId(),
      ...(hasTransient ? {
        transientAi: {
          appliedTransientName: appliedName,
          preClassifyName: preName,
          aiTransientTagPaths: [...transientPaths],
          aiTransientPartyIds: [...transientPartyIds],
        },
      } : {}),
    };
  }

  private loadActivity(): void {
    const id = this.documentId();
    if (!id) return;
    this.activityLoading.set(true);
    this.versionApi.getDocumentActivity({
      documentId: id,
      Page: 1,
      PageSize: 100,
    })
      .pipe(
        map((r) => r.items ?? []),
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
    this.versionApi.createComment({
      documentId: id,
      body: { body },
    }).pipe(
      handleMutationError(this.errorHandler, this.commentSubmitting, 'Failed to post comment.'),
    ).subscribe({
      next: () => {
        this.commentBody.set('');
        this.commentSubmitting.set(false);
        this.loadActivity();
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
