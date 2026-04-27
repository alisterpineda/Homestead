import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getCommodities } from '@/api/generated/fn/finances/get-commodities';
import { getCommodity } from '@/api/generated/fn/finances/get-commodity';
import type { PaginatedResponseOfCommodityResponse } from '@/api/generated/models/paginated-response-of-commodity-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

export type { GetCommodities$Params } from '@/api/generated/fn/finances/get-commodities';
export type { GetCommodity$Params } from '@/api/generated/fn/finances/get-commodity';

@Injectable({ providedIn: 'root' })
export class FinancesCommodityApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getCommodities(params: Parameters<typeof getCommodities>[2]): Observable<PaginatedResponseOfCommodityResponse> {
    return getCommodities(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getCommodity(params: Parameters<typeof getCommodity>[2]): Observable<CommodityResponse> {
    return getCommodity(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
