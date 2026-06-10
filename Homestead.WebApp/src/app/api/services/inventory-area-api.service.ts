import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getAreas } from '@/api/generated/fn/inventory/get-areas';
import { createArea } from '@/api/generated/fn/inventory/create-area';
import { patchArea } from '@/api/generated/fn/inventory/patch-area';
import { deleteArea } from '@/api/generated/fn/inventory/delete-area';
import type { PaginatedResponseOfAreaResponse } from '@/api/generated/models/paginated-response-of-area-response';
import type { AreaResponse } from '@/api/generated/models/area-response';

export type { GetAreas$Params } from '@/api/generated/fn/inventory/get-areas';
export type { CreateArea$Params } from '@/api/generated/fn/inventory/create-area';
export type { PatchArea$Params } from '@/api/generated/fn/inventory/patch-area';
export type { DeleteArea$Params } from '@/api/generated/fn/inventory/delete-area';

@Injectable({ providedIn: 'root' })
export class InventoryAreaApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getAreas(params: Parameters<typeof getAreas>[2]): Observable<PaginatedResponseOfAreaResponse> {
    return getAreas(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createArea(params: Parameters<typeof createArea>[2]): Observable<AreaResponse> {
    return createArea(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchArea(params: Parameters<typeof patchArea>[2]): Observable<AreaResponse> {
    return patchArea(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteArea(params: Parameters<typeof deleteArea>[2]): Observable<void> {
    return deleteArea(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
