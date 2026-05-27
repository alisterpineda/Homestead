import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEvent, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { createDocument } from '@/api/generated/fn/documents/create-document';
import { patchDocument } from '@/api/generated/fn/documents/patch-document';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
import { bulkDeleteDocuments } from '@/api/generated/fn/documents/bulk-delete-documents';
import { bulkReprocessDocuments } from '@/api/generated/fn/documents/bulk-reprocess-documents';
import { bulkUpdateDocuments } from '@/api/generated/fn/documents/bulk-update-documents';
import { bulkRestoreDocuments } from '@/api/generated/fn/documents/bulk-restore-documents';
import { bulkPermanentlyDeleteDocuments } from '@/api/generated/fn/documents/bulk-permanently-delete-documents';
import { emptyTrash } from '@/api/generated/fn/documents/empty-trash';
import { getMyGroups } from '@/api/generated/fn/documents/get-my-groups';
import { getDocumentSettings } from '@/api/generated/fn/documents/get-document-settings';
import { patchDocumentSettings } from '@/api/generated/fn/documents/patch-document-settings';
import { classifyPreview } from '@/api/generated/fn/documents/classify-preview';
import { bulkClassifyDocuments } from '@/api/generated/fn/documents/bulk-classify-documents';
import { approveSuggestion } from '@/api/generated/fn/documents/approve-suggestion';
import { declineSuggestion } from '@/api/generated/fn/documents/decline-suggestion';
import { approveAllSuggestions } from '@/api/generated/fn/documents/approve-all-suggestions';
import { declineAllSuggestions } from '@/api/generated/fn/documents/decline-all-suggestions';
import { getAiProviderSummaries } from '@/api/generated/fn/documents/get-ai-provider-summaries';
import type { GetDocumentsResult } from '@/api/generated/models/get-documents-result';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { CreateDocumentResponse } from '@/api/generated/models/create-document-response';
import type { PatchDocumentResponse } from '@/api/generated/models/patch-document-response';
import type { BulkDeleteDocumentsResponse } from '@/api/generated/models/bulk-delete-documents-response';
import type { BulkReprocessDocumentsResponse } from '@/api/generated/models/bulk-reprocess-documents-response';
import type { BulkUpdateDocumentsResponse } from '@/api/generated/models/bulk-update-documents-response';
import type { RestoreDocumentsResponse } from '@/api/generated/models/restore-documents-response';
import type { PermanentlyDeleteDocumentsResponse } from '@/api/generated/models/permanently-delete-documents-response';
import type { EmptyTrashResponse } from '@/api/generated/models/empty-trash-response';
import type { GetMyGroupsResponse } from '@/api/generated/models/get-my-groups-response';
import type { DocumentSettingsResponse } from '@/api/generated/models/document-settings-response';
import type { ClassifyPreviewResponse } from '@/api/generated/models/classify-preview-response';
import type { BulkClassifyDocumentsResponse } from '@/api/generated/models/bulk-classify-documents-response';
import type { AiProviderSummary } from '@/api/generated/models/ai-provider-summary';

export type { GetDocuments$Params } from '@/api/generated/fn/documents/get-documents';
export type { GetDocument$Params } from '@/api/generated/fn/documents/get-document';
export type { CreateDocument$Params } from '@/api/generated/fn/documents/create-document';
export type { PatchDocument$Params } from '@/api/generated/fn/documents/patch-document';
export type { DeleteDocument$Params } from '@/api/generated/fn/documents/delete-document';
export type { BulkDeleteDocuments$Params } from '@/api/generated/fn/documents/bulk-delete-documents';
export type { BulkReprocessDocuments$Params } from '@/api/generated/fn/documents/bulk-reprocess-documents';
export type { BulkUpdateDocuments$Params } from '@/api/generated/fn/documents/bulk-update-documents';
export type { BulkRestoreDocuments$Params } from '@/api/generated/fn/documents/bulk-restore-documents';
export type { BulkPermanentlyDeleteDocuments$Params } from '@/api/generated/fn/documents/bulk-permanently-delete-documents';
export type { EmptyTrash$Params } from '@/api/generated/fn/documents/empty-trash';
export type { GetMyGroups$Params } from '@/api/generated/fn/documents/get-my-groups';
export type { PatchDocumentSettings$Params } from '@/api/generated/fn/documents/patch-document-settings';
export type { ClassifyPreview$Params } from '@/api/generated/fn/documents/classify-preview';
export type { BulkClassifyDocuments$Params } from '@/api/generated/fn/documents/bulk-classify-documents';
export type { ApproveSuggestion$Params } from '@/api/generated/fn/documents/approve-suggestion';
export type { DeclineSuggestion$Params } from '@/api/generated/fn/documents/decline-suggestion';
export type { ApproveAllSuggestions$Params } from '@/api/generated/fn/documents/approve-all-suggestions';
export type { DeclineAllSuggestions$Params } from '@/api/generated/fn/documents/decline-all-suggestions';

@Injectable({ providedIn: 'root' })
export class DocumentApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getDocuments(params: Parameters<typeof getDocuments>[2]): Observable<GetDocumentsResult> {
    return getDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDocument(params: Parameters<typeof getDocument>[2]): Observable<GetDocumentResponse> {
    return getDocument(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createDocument(params: Parameters<typeof createDocument>[2]): Observable<CreateDocumentResponse> {
    return createDocument(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchDocument(params: Parameters<typeof patchDocument>[2]): Observable<PatchDocumentResponse> {
    return patchDocument(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteDocument(params: Parameters<typeof deleteDocument>[2]): Observable<void> {
    return deleteDocument(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteDocuments(params: Parameters<typeof bulkDeleteDocuments>[2]): Observable<BulkDeleteDocumentsResponse> {
    return bulkDeleteDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkReprocessDocuments(params: Parameters<typeof bulkReprocessDocuments>[2]): Observable<BulkReprocessDocumentsResponse> {
    return bulkReprocessDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkUpdateDocuments(params: Parameters<typeof bulkUpdateDocuments>[2]): Observable<BulkUpdateDocumentsResponse> {
    return bulkUpdateDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkRestoreDocuments(params: Parameters<typeof bulkRestoreDocuments>[2]): Observable<RestoreDocumentsResponse> {
    return bulkRestoreDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkPermanentlyDeleteDocuments(params: Parameters<typeof bulkPermanentlyDeleteDocuments>[2]): Observable<PermanentlyDeleteDocumentsResponse> {
    return bulkPermanentlyDeleteDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  emptyTrash(): Observable<EmptyTrashResponse> {
    return emptyTrash(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  getMyGroups(): Observable<Array<GetMyGroupsResponse>> {
    return getMyGroups(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  getDocumentSettings(): Observable<DocumentSettingsResponse> {
    return getDocumentSettings(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  patchDocumentSettings(params: Parameters<typeof patchDocumentSettings>[2]): Observable<DocumentSettingsResponse> {
    return patchDocumentSettings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  classifyPreview(params: Parameters<typeof classifyPreview>[2]): Observable<ClassifyPreviewResponse> {
    return classifyPreview(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkClassifyDocuments(params: Parameters<typeof bulkClassifyDocuments>[2]): Observable<BulkClassifyDocumentsResponse> {
    return bulkClassifyDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  approveSuggestion(params: Parameters<typeof approveSuggestion>[2]): Observable<void> {
    return approveSuggestion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  declineSuggestion(params: Parameters<typeof declineSuggestion>[2]): Observable<void> {
    return declineSuggestion(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  approveAllSuggestions(params: Parameters<typeof approveAllSuggestions>[2]): Observable<void> {
    return approveAllSuggestions(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  declineAllSuggestions(params: Parameters<typeof declineAllSuggestions>[2]): Observable<void> {
    return declineAllSuggestions(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getAiProviderSummaries(): Observable<Array<AiProviderSummary>> {
    return getAiProviderSummaries(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  getDocumentFile(id: string, versionId?: string | null): Observable<Blob> {
    const url = this.getDocumentFileUrl(id, versionId);
    return this.http.get(url, { responseType: 'blob' });
  }

  getDocumentFileUrl(id: string, versionId?: string | null): string {
    return this.buildVersionedUrl(id, 'file', versionId);
  }

  getDocumentThumbnailUrl(id: string, versionId?: string | null): string {
    return this.buildVersionedUrl(id, 'thumbnail', versionId);
  }

  private buildVersionedUrl(id: string, segment: 'file' | 'thumbnail', versionId?: string | null): string {
    const base = `${this.config.rootUrl}/api/v1/documents/${id}/${segment}`;
    return versionId == null ? base : `${base}?versionId=${encodeURIComponent(versionId)}`;
  }

  uploadDocument(file: File): Observable<HttpEvent<CreateDocumentResponse>> {
    const formData = new FormData();
    formData.append('file', file);
    const req = new HttpRequest<FormData>(
      'POST',
      `${this.config.rootUrl}/api/v1/documents`,
      formData,
      { reportProgress: true },
    );
    return this.http.request<CreateDocumentResponse>(req);
  }
}
