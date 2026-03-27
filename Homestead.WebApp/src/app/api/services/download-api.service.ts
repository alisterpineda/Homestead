import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { downloadDocuments } from '@/api/generated/fn/documents/download-documents';
import type { DownloadDocumentsResponse } from '@/api/generated/models/download-documents-response';

export type { DownloadDocuments$Params } from '@/api/generated/fn/documents/download-documents';

@Injectable({ providedIn: 'root' })
export class DownloadApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  downloadDocuments(params: Parameters<typeof downloadDocuments>[2]): Observable<DownloadDocumentsResponse> {
    return downloadDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDownloadUrl(jobId: string): string {
    return `${this.config.rootUrl}/api/v1/documents/download/${jobId}`;
  }
}
