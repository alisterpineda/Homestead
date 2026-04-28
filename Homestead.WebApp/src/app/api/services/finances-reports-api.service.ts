import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getNetWorthHistory } from '@/api/generated/fn/finances/get-net-worth-history';
import type { GetNetWorthHistoryResponse } from '@/api/generated/models/get-net-worth-history-response';

export type { GetNetWorthHistory$Params } from '@/api/generated/fn/finances/get-net-worth-history';

@Injectable({ providedIn: 'root' })
export class FinancesReportsApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getNetWorthHistory(
    params: Parameters<typeof getNetWorthHistory>[2],
  ): Observable<GetNetWorthHistoryResponse> {
    return getNetWorthHistory(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
