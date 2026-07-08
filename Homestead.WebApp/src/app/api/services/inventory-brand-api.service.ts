import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getBrands } from '@/api/generated/fn/inventory/get-brands';
import { createBrand } from '@/api/generated/fn/inventory/create-brand';
import type { PaginatedResponseOfBrandResponse } from '@/api/generated/models/paginated-response-of-brand-response';
import type { BrandResponse } from '@/api/generated/models/brand-response';

export type { GetBrands$Params } from '@/api/generated/fn/inventory/get-brands';
export type { CreateBrand$Params } from '@/api/generated/fn/inventory/create-brand';

@Injectable({ providedIn: 'root' })
export class InventoryBrandApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getBrands(params: Parameters<typeof getBrands>[2]): Observable<PaginatedResponseOfBrandResponse> {
    return getBrands(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createBrand(params: Parameters<typeof createBrand>[2]): Observable<BrandResponse> {
    return createBrand(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
