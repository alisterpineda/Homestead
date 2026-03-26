import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { exportDocuments } from '@/api/generated/fn/documents/export-documents';
import { getExports } from '@/api/generated/fn/documents/get-exports';
import type { ExportDocumentsResponse } from '@/api/generated/models/export-documents-response';
import type { PaginatedResponseOfGetExportsResponse } from '@/api/generated/models/paginated-response-of-get-exports-response';

export type { ExportDocuments$Params } from '@/api/generated/fn/documents/export-documents';
export type { GetExports$Params } from '@/api/generated/fn/documents/get-exports';

@Injectable({ providedIn: 'root' })
export class ExportApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  exportDocuments(params: Parameters<typeof exportDocuments>[2]): Observable<ExportDocumentsResponse> {
    return exportDocuments(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getExports(params: Parameters<typeof getExports>[2]): Observable<PaginatedResponseOfGetExportsResponse> {
    return getExports(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDownloadUrl(jobId: string): string {
    return `${this.config.rootUrl}/api/v1/documents/export/${jobId}/download`;
  }
}
