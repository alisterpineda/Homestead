import { Component, ChangeDetectionStrategy, input, output, signal, inject, LOCALE_ID } from '@angular/core';
import { formatDate as ngFormatDate } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideFileText, lucideImage, lucideUsers } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';
import type { GetDocumentsTagDto } from '@/api/generated/models/get-documents-tag-dto';
import { TagBadgeComponent } from '../../tags/tag-badge/tag-badge.component';
import { MatchSnippetComponent } from '../match-snippet/match-snippet.component';

@Component({
  selector: 'app-document-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmCardImports,
    HlmCheckbox,
    HlmBadge,
    TagBadgeComponent,
    MatchSnippetComponent,
  ],
  providers: [provideIcons({ lucideCalendar, lucideFileText, lucideImage, lucideUsers })],
  styles: `
    .scrim-overlay {
      background: linear-gradient(in oklch to top,
        oklch(0% 0 0 / 0.9) 0%, oklch(0% 0 0 / 0.82) 10%,
        oklch(0% 0 0 / 0.72) 20%, oklch(0% 0 0 / 0.6) 32%,
        oklch(0% 0 0 / 0.46) 44%, oklch(0% 0 0 / 0.32) 56%,
        oklch(0% 0 0 / 0.18) 70%, oklch(0% 0 0 / 0.08) 85%,
        oklch(0% 0 0 / 0) 100%);
    }
    .text-shadow-scrim {
      text-shadow: 0 1px 3px rgba(0, 0, 0, 0.8);
    }
    .tag-card-badge {
      filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.3));
    }
    :host ::ng-deep .match-snippet mark {
      background-color: oklch(1 0 0 / 0.25);
      color: white;
    }
  `,
  template: `
    <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-3">
      @for (doc of documents(); track doc.id) {
        <div
          hlmCard
          class="group relative aspect-[3/4] cursor-pointer overflow-hidden p-0 gap-0 transition-shadow hover:shadow-md"
          tabindex="0"
          role="button"
          [attr.aria-label]="doc.name"
          (click)="onCardClick(doc.id)"
          (keydown.enter)="onCardClick(doc.id)"
        >
          <!-- Selection border overlay -->
          @if (isSelected(doc.id)) {
            <div class="pointer-events-none absolute inset-0 z-20 rounded-[inherit] border-2 border-primary"></div>
          }

          <!-- Full-bleed thumbnail -->
          @if (doc.hasThumbnail && !hasError(doc.id)) {
            <img
              [src]="getThumbnailUrl(doc.id)"
              [alt]="doc.name"
              loading="lazy"
              class="absolute inset-0 h-full w-full object-cover"
              (error)="onImageError(doc.id)"
            />
          } @else {
            <div class="absolute inset-0 bg-muted flex items-center justify-center">
              <ng-icon
                hlm
                [name]="isImageType(doc.contentType) ? 'lucideImage' : 'lucideFileText'"
                size="xl"
                class="text-muted-foreground"
              />
            </div>
          }

          <!-- Checkbox overlay -->
          <div
            class="absolute top-0 left-0 z-10 p-2 rounded-br-lg bg-background/80 shadow-sm backdrop-blur-sm transition-opacity duration-150 group-hover:opacity-100"
            [class.opacity-100]="selectionMode() || isSelected(doc.id)"
            [class.opacity-0]="!selectionMode() && !isSelected(doc.id)"
            (click)="$event.stopPropagation()"
            (keydown.enter)="$event.stopPropagation()"
          >
            <hlm-checkbox
              [checked]="isSelected(doc.id)"
              [aria-label]="'Select ' + doc.name"
              (checkedChange)="onCheckboxChange(doc.id, $event)"
            />
          </div>

          <!-- Tag overlay -->
          @if (doc.tags.length > 0) {
            <div class="absolute top-2 left-10 right-2 z-10 flex flex-col items-end gap-1 pointer-events-none">
              @for (tag of visibleTags(doc.tags); track tag.id) {
                <app-tag-badge
                  class="tag-card-badge max-w-full pointer-events-auto"
                  [path]="tag.path"
                  [color]="tag.effectiveColor"
                  [compact]="true"
                  [title]="tag.path"
                />
              }
              @if (doc.tags.length > maxVisibleTags) {
                <span
                  hlmBadge
                  variant="secondary"
                  class="tag-card-badge pointer-events-auto"
                  [attr.aria-label]="'and ' + (doc.tags.length - maxVisibleTags) + ' more tags'"
                >
                  +{{ doc.tags.length - maxVisibleTags }}
                </span>
              }
            </div>
          }

          <!-- Bottom metadata overlay -->
          <div
            class="scrim-overlay absolute bottom-0 inset-x-0 p-3"
            [class.pt-20]="!doc.matchSnippet"
            [class.pt-24]="doc.matchSnippet"
          >
            <!-- Detail lines above the title -->
            @if (doc.documentDate) {
              <div class="h-4 mb-0.5 flex items-center gap-1 overflow-hidden">
                <ng-icon hlm name="lucideCalendar" size="xs" class="shrink-0 text-white/90" />
                <p class="text-shadow-scrim text-white/90 text-xs truncate">{{ formatDate(doc.documentDate) }}</p>
              </div>
            }
            @if (doc.parties.length > 0) {
              <div class="h-4 mb-0.5 flex items-center gap-1 overflow-hidden">
                <ng-icon hlm name="lucideUsers" size="xs" class="shrink-0 text-white/90" />
                <p class="text-shadow-scrim text-white/90 text-xs truncate">{{ getPartyNames(doc.parties) }}</p>
              </div>
            }
            @if (doc.matchSnippet) {
              <app-match-snippet
                class="text-shadow-scrim text-white/90 text-xs mb-0.5"
                [html]="doc.matchSnippet"
              />
            }
            <!-- Title always at the bottom -->
            <p class="text-shadow-scrim text-white font-medium truncate text-sm" [title]="doc.name">{{ doc.name }}</p>
          </div>
        </div>
      }
    </div>
  `,
})
export class DocumentCardGridComponent {
  private documentApi = inject(DocumentApiService);
  private locale = inject(LOCALE_ID);

  documents = input.required<GetDocumentsResponse[]>();
  selectedIds = input<Set<string>>(new Set());
  selectionMode = input(false);

  documentSelect = output<string>();
  selectionChange = output<Set<string>>();

  readonly maxVisibleTags = 3;

  private thumbnailErrors = signal<Set<string>>(new Set());

  visibleTags(tags: GetDocumentsTagDto[]): GetDocumentsTagDto[] {
    return tags.slice(0, this.maxVisibleTags);
  }

  isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  hasError(id: string): boolean {
    return this.thumbnailErrors().has(id);
  }

  getThumbnailUrl(docId: string): string {
    return this.documentApi.getDocumentThumbnailUrl(docId);
  }

  isImageType(contentType: string): boolean {
    return contentType.startsWith('image/');
  }

  formatDate(date: string): string {
    return ngFormatDate(date, 'mediumDate', this.locale);
  }

  getPartyNames(parties: { name: string }[]): string {
    return parties.map(p => p.name).join(', ');
  }

  onCardClick(id: string): void {
    if (this.selectionMode() || this.selectedIds().size > 0) {
      this.onCheckboxChange(id, !this.isSelected(id));
    } else {
      this.documentSelect.emit(id);
    }
  }

  onCheckboxChange(id: string, checked: boolean): void {
    const current = new Set(this.selectedIds());
    if (checked) {
      current.add(id);
    } else {
      current.delete(id);
    }
    this.selectionChange.emit(current);
  }

  onImageError(id: string): void {
    this.thumbnailErrors.update(set => {
      const next = new Set(set);
      next.add(id);
      return next;
    });
  }
}
