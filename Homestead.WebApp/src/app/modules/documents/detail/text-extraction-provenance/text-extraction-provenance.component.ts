import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import type { TextExtractionProvenanceResponse } from '@/api/generated/models/text-extraction-provenance-response';

const TEXT_LAYER_EXTRACTORS: Record<string, string> = {
  poppler: 'pdftotext -layout',
  pdfpig: 'PdfPig',
};

/**
 * How a version's extracted text was produced. Read-only, and shown only when provenance exists —
 * versions extracted before this was recorded simply show nothing.
 */
@Component({
  selector: 'app-text-extraction-provenance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon],
  providers: [provideIcons({ lucideTriangleAlert })],
  template: `
    @if (provenance(); as p) {
      <div class="flex flex-col gap-1 text-sm text-muted-foreground">
        <div>
          Extracted by <span class="font-medium text-foreground">{{ p.engineKey }}</span>
          on {{ p.extractedAt | date: 'medium' }}
        </div>

        <div>{{ detail() }}</div>

        @if (p.requestedEngineKey) {
          <div class="flex items-start gap-2 rounded-md border border-border bg-muted p-2">
            <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(4)] mt-0.5 shrink-0" />
            <span>
              <span class="font-medium text-foreground">{{ p.requestedEngineKey }}</span> was requested but
              unavailable ({{ p.fallbackReason }}), so {{ p.engineKey }} ran instead.
            </span>
          </div>
        }
      </div>
    }
  `,
})
export class TextExtractionProvenanceComponent {
  provenance = input.required<TextExtractionProvenanceResponse | null | undefined>();

  // The engine-specific line. For Tesseract the page split is the interesting part — it reveals which
  // branch of the hybrid PDF path each page took, which is otherwise unknowable after the fact.
  detail = computed(() => {
    const p = this.provenance();
    if (!p) return '';

    if (p.engine === 'tesseract') {
      const parts = [`Tesseract (${p.language})`];
      if (p.pagesFromTextLayer || p.pagesOcred) {
        // Which tool read the text layer: Poppler keeps column alignment, PdfPig (the fallback) flattens it.
        const extractor = p.pagesFromTextLayer ? TEXT_LAYER_EXTRACTORS[p.textLayerExtractor ?? ''] : undefined;
        const source = extractor ? `embedded text layer (${extractor})` : 'embedded text layer';
        parts.push(`${p.pagesFromTextLayer ?? 0} page(s) from ${source}, ${p.pagesOcred ?? 0} OCR'd`);
      }
      return parts.join(' · ');
    }

    if (p.engine === 'azure-document-intelligence') {
      const parts = [`Azure Document Intelligence (${p.modelId})`];
      if (p.outputContentFormat) parts.push(`${p.outputContentFormat} output`);
      if (p.pageCount) parts.push(`${p.pageCount} page(s) analyzed`);
      return parts.join(' · ');
    }

    return p.engine;
  });
}
