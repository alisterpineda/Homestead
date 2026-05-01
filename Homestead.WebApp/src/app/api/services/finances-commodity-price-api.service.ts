import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getCommodityPrices } from '@/api/generated/fn/finances/get-commodity-prices';
import { createCommodityPrice } from '@/api/generated/fn/finances/create-commodity-price';
import { patchCommodityPrice } from '@/api/generated/fn/finances/patch-commodity-price';
import { deleteCommodityPrice } from '@/api/generated/fn/finances/delete-commodity-price';
import { bulkDeleteCommodityPrices } from '@/api/generated/fn/finances/bulk-delete-commodity-prices';
import type { PaginatedResponseOfCommodityPriceResponse } from '@/api/generated/models/paginated-response-of-commodity-price-response';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';
import type { DeleteCommodityPricesResponse } from '@/api/generated/models/delete-commodity-prices-response';

export type { GetCommodityPrices$Params } from '@/api/generated/fn/finances/get-commodity-prices';
export type { CreateCommodityPrice$Params } from '@/api/generated/fn/finances/create-commodity-price';
export type { PatchCommodityPrice$Params } from '@/api/generated/fn/finances/patch-commodity-price';
export type { DeleteCommodityPrice$Params } from '@/api/generated/fn/finances/delete-commodity-price';
export type { BulkDeleteCommodityPrices$Params } from '@/api/generated/fn/finances/bulk-delete-commodity-prices';

@Injectable({ providedIn: 'root' })
export class FinancesCommodityPriceApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getCommodityPrices(params: Parameters<typeof getCommodityPrices>[2]): Observable<PaginatedResponseOfCommodityPriceResponse> {
    return getCommodityPrices(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createCommodityPrice(params: Parameters<typeof createCommodityPrice>[2]): Observable<CommodityPriceResponse> {
    return createCommodityPrice(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchCommodityPrice(params: Parameters<typeof patchCommodityPrice>[2]): Observable<CommodityPriceResponse> {
    return patchCommodityPrice(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteCommodityPrice(params: Parameters<typeof deleteCommodityPrice>[2]): Observable<void> {
    return deleteCommodityPrice(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteCommodityPrices(params: Parameters<typeof bulkDeleteCommodityPrices>[2]): Observable<DeleteCommodityPricesResponse> {
    return bulkDeleteCommodityPrices(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
