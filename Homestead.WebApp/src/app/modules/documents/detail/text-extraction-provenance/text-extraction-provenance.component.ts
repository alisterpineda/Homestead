import { Component, ChangeDetectionStrategy, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucideTriangleAlert } from '@ng-icons/lucide';
import type { TextExtractionProvenanceResponse } from '@/api/generated/models/text-extraction-provenance-response';

const TEXT_LAYER_EXTRACTORS: Record<string, string> = {
  poppler: 'pdftotext -layout',
  pdfpig: 'PdfPig',
};

/**
 * How a version's stored text was produced. Read-only, and shown only when provenance exists —
 * versions extracted before this was recorded simply show nothing. An engine run reads "Extracted by";
 * text a person hand-edited reads "Edited by", with the engine run it started from as its basis, so
 * edited Docling output is never mistaken for Docling output.
 */
@Component({
  selector: 'app-text-extraction-provenance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgIcon],
  providers: [provideIcons({ lucidePencil, lucideTriangleAlert })],
  template: `
    @if (provenance(); as p) {
      <div class="flex flex-col gap-1 text-sm text-muted-foreground">
        @if (p.engine === 'manual') {
          <div class="flex items-center gap-1.5">
            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)] shrink-0" aria-hidden="true" />
            <span>
              Edited by <span class="font-medium text-foreground">{{ p.editedByDisplayName || 'Someone' }}</span>
              on {{ p.extractedAt | date: 'medium' }}
            </span>
          </div>

          @if (p.basisEngineKey) {
            <div>
              Based on <span class="font-medium text-foreground">{{ p.basisEngineKey }}</span> extraction from
              {{ p.basisExtractedAt | date: 'medium' }}
            </div>
          } @else {
            <div>Entered manually, no engine extraction</div>
          }
        } @else {
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

    if (p.engine === 'docling') {
      // Recorded without a request: the file type is one Docling does not convert, so nothing ran.
      if (p.conversionStatus === 'unsupported_content_type') return 'Docling · not converted (unsupported file type)';

      // No table mode recorded means the server default ran, which is accurate.
      const parts = ['Docling', `${p.tableMode ?? 'accurate'} tables`];
      if (p.forceOcr) parts.push('forced OCR');
      if (p.pageCount) parts.push(`${p.pageCount} page(s)`);
      if (p.processingSeconds != null) parts.push(`${Math.round(p.processingSeconds * 10) / 10}s`);
      // The durable sign that the stored text is incomplete — some pages or tables failed to convert.
      if (p.conversionStatus === 'partial_success') parts.push('partial: some content could not be converted');
      return parts.join(' · ');
    }

    return p.engine;
  });
}
