import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { universalSearch } from '@/api/generated/fn/search/universal-search';
import type { SearchResponse } from '@/api/generated/models/search-response';

export type { UniversalSearch$Params } from '@/api/generated/fn/search/universal-search';

@Injectable({ providedIn: 'root' })
export class SearchApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  search(params: Parameters<typeof universalSearch>[2]): Observable<SearchResponse> {
    return universalSearch(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
