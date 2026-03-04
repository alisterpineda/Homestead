import { Component, ChangeDetectionStrategy, input, output, signal, inject } from '@angular/core';
import { SlicePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucideImage } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-document-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SlicePipe,
    NgIcon,
    HlmIconImports,
    HlmCardImports,
    HlmCheckbox,
    TagBadgeComponent,
    PartyBadgeComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucideImage })],
  template: `
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
      @for (doc of documents(); track doc.id) {
        <div
          hlmCard
          class="cursor-pointer overflow-hidden transition-shadow hover:shadow-md"
          [class]="isSelected(doc.id) ? 'ring-2 ring-primary' : ''"
          tabindex="0"
          role="button"
          [attr.aria-label]="doc.name"
          (click)="onCardClick(doc.id)"
          (keydown.enter)="onCardClick(doc.id)"
        >
          <!-- Thumbnail area -->
          <div class="relative aspect-[3/2] bg-muted flex items-center justify-center overflow-hidden">
            <!-- Checkbox overlay -->
            <div class="absolute top-2 left-2 z-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
              <hlm-checkbox
                [checked]="isSelected(doc.id)"
                [aria-label]="'Select ' + doc.name"
                (checkedChange)="onCheckboxChange(doc.id, $event)"
              />
            </div>

            @if (doc.hasThumbnail && !hasError(doc.id)) {
              <img
                [src]="getThumbnailUrl(doc.id)"
                [alt]="doc.name"
                loading="lazy"
                class="h-full w-full object-cover"
                (error)="onImageError(doc.id)"
              />
            } @else {
              <ng-icon
                hlm
                [name]="isImageType(doc.contentType) ? 'lucideImage' : 'lucideFileText'"
                size="xl"
                class="text-muted-foreground"
              />
            }
          </div>

          <!-- Content -->
          <div hlmCardContent class="p-3">
            <p class="font-medium truncate text-sm" [title]="doc.name">{{ doc.name }}</p>
            <p class="text-xs text-muted-foreground truncate">{{ doc.contentType }}</p>
          </div>

          <!-- Footer with tags and parties -->
          @if (doc.tags.length > 0 || doc.parties.length > 0) {
            <div hlmCardFooter class="px-3 pb-3 pt-0 flex flex-wrap gap-1">
              @for (tag of doc.tags | slice:0:3; track tag.id) {
                <app-tag-badge [path]="tag.path" [color]="tag.color" />
              }
              @for (party of doc.parties | slice:0:2; track party.id) {
                <app-party-badge [name]="party.name" />
              }
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class DocumentCardGridComponent {
  private apiConfig = inject(ApiConfiguration);

  documents = input.required<GetDocumentsResponse[]>();
  selectedIds = input<Set<string>>(new Set());

  documentSelect = output<string>();
  selectionChange = output<Set<string>>();

  private thumbnailErrors = signal<Set<string>>(new Set());

  isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  hasError(id: string): boolean {
    return this.thumbnailErrors().has(id);
  }

  getThumbnailUrl(docId: string): string {
    return `${this.apiConfig.rootUrl}/api/v1/documents/${docId}/thumbnail`;
  }

  isImageType(contentType: string): boolean {
    return contentType.startsWith('image/');
  }

  onCardClick(id: string): void {
    this.documentSelect.emit(id);
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
