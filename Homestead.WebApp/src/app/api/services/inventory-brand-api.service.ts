import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getBrands } from '@/api/generated/fn/inventory/get-brands';
import { createBrand } from '@/api/generated/fn/inventory/create-brand';
import { patchBrand } from '@/api/generated/fn/inventory/patch-brand';
import { deleteBrand } from '@/api/generated/fn/inventory/delete-brand';
import { mergeBrand } from '@/api/generated/fn/inventory/merge-brand';
import type { PaginatedResponseOfBrandResponse } from '@/api/generated/models/paginated-response-of-brand-response';
import type { BrandResponse } from '@/api/generated/models/brand-response';

export type { GetBrands$Params } from '@/api/generated/fn/inventory/get-brands';
export type { CreateBrand$Params } from '@/api/generated/fn/inventory/create-brand';
export type { PatchBrand$Params } from '@/api/generated/fn/inventory/patch-brand';
export type { DeleteBrand$Params } from '@/api/generated/fn/inventory/delete-brand';
export type { MergeBrand$Params } from '@/api/generated/fn/inventory/merge-brand';

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

  patchBrand(params: Parameters<typeof patchBrand>[2]): Observable<BrandResponse> {
    return patchBrand(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteBrand(params: Parameters<typeof deleteBrand>[2]): Observable<void> {
    return deleteBrand(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  mergeBrand(params: Parameters<typeof mergeBrand>[2]): Observable<BrandResponse> {
    return mergeBrand(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
