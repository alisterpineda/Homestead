import { Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of, shareReplay } from 'rxjs';

import { DocumentApiService } from '@/api/services/document-api.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

/**
 * The configured text extraction engines, fetched once and shared. Engines are deployment config, so the
 * list cannot change during a session — and several surfaces need it at once (the picker itself, plus the
 * bulk dialog's cost warning), which is why it is a service rather than a fetch per component.
 */
@Injectable({ providedIn: 'root' })
export class TextExtractionEnginesService {
  private documentApi = inject(DocumentApiService);

  readonly engines = toSignal(
    this.documentApi.getTextExtractionEngines().pipe(
      // An empty list degrades the picker to "household default" only, rather than breaking the dialog
      // it is embedded in.
      catchError(() => of<TextExtractionEngineResponse[]>([])),
      shareReplay(1),
    ),
    { initialValue: [] as TextExtractionEngineResponse[] },
  );

  find(key: string): TextExtractionEngineResponse | undefined {
    return this.engines().find(e => e.key === key);
  }

  /**
   * Whether picking this engine bills per page. Tesseract runs locally and is free; Azure Document
   * Intelligence is charged per page, which is what makes an unbounded bulk reprocess worth warning about.
   */
  isMetered(key: string): boolean {
    return this.find(key)?.type === 'AzureDocumentIntelligence';
  }
}
