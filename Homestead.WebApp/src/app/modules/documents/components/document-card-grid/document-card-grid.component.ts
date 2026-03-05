import { Component, ChangeDetectionStrategy, input, output, signal, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucideImage, lucideUsers } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-document-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmCardImports,
    HlmCheckbox,
  ],
  providers: [provideIcons({ lucideFileText, lucideImage, lucideUsers })],
  styles: `
    .scrim-overlay {
      background: linear-gradient(in oklch to top,
        oklch(0% 0 0 / 0.85) 0%, oklch(0% 0 0 / 0.72) 12%,
        oklch(0% 0 0 / 0.58) 24%, oklch(0% 0 0 / 0.42) 36%,
        oklch(0% 0 0 / 0.28) 48%, oklch(0% 0 0 / 0.18) 58%,
        oklch(0% 0 0 / 0.10) 68%, oklch(0% 0 0 / 0.04) 80%,
        oklch(0% 0 0 / 0) 100%);
    }
    .text-shadow-scrim {
      text-shadow: 0 1px 3px rgba(0, 0, 0, 0.8);
    }
  `,
  template: `
    <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-3">
      @for (doc of documents(); track doc.id) {
        <div
          hlmCard
          class="relative aspect-[3/4] cursor-pointer overflow-hidden p-0 gap-0 transition-shadow hover:shadow-md"
          [class]="isSelected(doc.id) ? 'ring-2 ring-primary' : ''"
          tabindex="0"
          role="button"
          [attr.aria-label]="doc.name"
          (click)="onCardClick(doc.id)"
          (keydown.enter)="onCardClick(doc.id)"
        >
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
          <div class="absolute top-2 left-2 z-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
            <hlm-checkbox
              class="bg-background shadow-sm"
              [checked]="isSelected(doc.id)"
              [aria-label]="'Select ' + doc.name"
              (checkedChange)="onCheckboxChange(doc.id, $event)"
            />
          </div>

          <!-- Bottom metadata overlay -->
          <div class="scrim-overlay absolute bottom-0 inset-x-0 p-3 pt-12">
            <!-- Detail lines (parties, future fields) above the title — always reserved -->
            <div class="h-4 mb-0.5 flex items-center gap-1 overflow-hidden">
              @if (doc.parties.length > 0) {
                <ng-icon hlm name="lucideUsers" size="xs" class="shrink-0 text-white/70" />
                <p class="text-shadow-scrim text-white/70 text-xs truncate">{{ getPartyNames(doc.parties) }}</p>
              }
            </div>
            <!-- Title always at the bottom -->
            <p class="text-shadow-scrim text-white font-medium truncate text-sm" [title]="doc.name">{{ doc.name }}</p>
          </div>
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

  getPartyNames(parties: { name: string }[]): string {
    return parties.map(p => p.name).join(', ');
  }

  onCardClick(id: string): void {
    if (this.selectedIds().size > 0) {
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
