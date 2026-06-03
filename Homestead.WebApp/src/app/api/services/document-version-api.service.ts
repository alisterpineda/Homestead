import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocumentVersion } from '@/api/generated/fn/documents/get-document-version';
import { patchDocumentVersion } from '@/api/generated/fn/documents/patch-document-version';
import { reprocessDocumentVersion } from '@/api/generated/fn/documents/reprocess-document-version';
import { createDocumentVersion } from '@/api/generated/fn/documents/create-document-version';
import { deleteDocumentVersion } from '@/api/generated/fn/documents/delete-document-version';
import { getDocumentActivity } from '@/api/generated/fn/documents/get-document-activity';
import { createComment } from '@/api/generated/fn/documents/create-comment';
import { getTextExtractionEngineSummaries } from '@/api/generated/fn/documents/get-text-extraction-engine-summaries';
import type { GetDocumentVersionResponse } from '@/api/generated/models/get-document-version-response';
import type { PatchDocumentVersionResponse } from '@/api/generated/models/patch-document-version-response';
import type { ReprocessDocumentVersionResponse } from '@/api/generated/models/reprocess-document-version-response';
import type { CreateDocumentVersionResponse } from '@/api/generated/models/create-document-version-response';
import type { PaginatedResponseOfGetActivityResponse } from '@/api/generated/models/paginated-response-of-get-activity-response';
import type { CreateCommentResponse } from '@/api/generated/models/create-comment-response';
import type { TextExtractionEngineSummary } from '@/api/generated/models/text-extraction-engine-summary';

export type { GetDocumentVersion$Params } from '@/api/generated/fn/documents/get-document-version';
export type { PatchDocumentVersion$Params } from '@/api/generated/fn/documents/patch-document-version';
export type { ReprocessDocumentVersion$Params } from '@/api/generated/fn/documents/reprocess-document-version';
export type { CreateDocumentVersion$Params } from '@/api/generated/fn/documents/create-document-version';
export type { DeleteDocumentVersion$Params } from '@/api/generated/fn/documents/delete-document-version';
export type { GetDocumentActivity$Params } from '@/api/generated/fn/documents/get-document-activity';
export type { CreateComment$Params } from '@/api/generated/fn/documents/create-comment';
export type { GetTextExtractionEngineSummaries$Params } from '@/api/generated/fn/documents/get-text-extraction-engine-summaries';

@Injectable({ providedIn: 'root' })
export class DocumentVersionApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getDocumentVersion(params: Parameters<typeof getDocumentVersion>[2]): Observable<GetDocumentVersionResponse> {
    return getDocumentVersion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchDocumentVersion(params: Parameters<typeof patchDocumentVersion>[2]): Observable<PatchDocumentVersionResponse> {
    return patchDocumentVersion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  reprocessDocumentVersion(params: Parameters<typeof reprocessDocumentVersion>[2]): Observable<ReprocessDocumentVersionResponse> {
    return reprocessDocumentVersion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createDocumentVersion(params: Parameters<typeof createDocumentVersion>[2]): Observable<CreateDocumentVersionResponse> {
    return createDocumentVersion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteDocumentVersion(params: Parameters<typeof deleteDocumentVersion>[2]): Observable<void> {
    return deleteDocumentVersion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDocumentActivity(params: Parameters<typeof getDocumentActivity>[2]): Observable<PaginatedResponseOfGetActivityResponse> {
    return getDocumentActivity(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createComment(params: Parameters<typeof createComment>[2]): Observable<CreateCommentResponse> {
    return createComment(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getTextExtractionEngineSummaries(): Observable<Array<TextExtractionEngineSummary>> {
    return getTextExtractionEngineSummaries(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }
}
