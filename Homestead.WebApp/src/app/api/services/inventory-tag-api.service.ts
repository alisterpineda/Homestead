import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getInventoryTags } from '@/api/generated/fn/inventory/get-inventory-tags';
import { getInventoryTagsByIds } from '@/api/generated/fn/inventory/get-inventory-tags-by-ids';
import { createInventoryTag } from '@/api/generated/fn/inventory/create-inventory-tag';
import { patchInventoryTag } from '@/api/generated/fn/inventory/patch-inventory-tag';
import { bulkDeleteInventoryTags } from '@/api/generated/fn/inventory/bulk-delete-inventory-tags';
import type { PaginatedResponseOfGetInventoryTagsResponse } from '@/api/generated/models/paginated-response-of-get-inventory-tags-response';
import type { GetInventoryTagsByIdsResponse } from '@/api/generated/models/get-inventory-tags-by-ids-response';
import type { CreateInventoryTagResponse } from '@/api/generated/models/create-inventory-tag-response';
import type { PatchInventoryTagResponse } from '@/api/generated/models/patch-inventory-tag-response';
import type { BulkDeleteInventoryTagsResponse } from '@/api/generated/models/bulk-delete-inventory-tags-response';

export type { GetInventoryTags$Params } from '@/api/generated/fn/inventory/get-inventory-tags';
export type { GetInventoryTagsByIds$Params } from '@/api/generated/fn/inventory/get-inventory-tags-by-ids';
export type { CreateInventoryTag$Params } from '@/api/generated/fn/inventory/create-inventory-tag';
export type { PatchInventoryTag$Params } from '@/api/generated/fn/inventory/patch-inventory-tag';
export type { BulkDeleteInventoryTags$Params } from '@/api/generated/fn/inventory/bulk-delete-inventory-tags';

@Injectable({ providedIn: 'root' })
export class InventoryTagApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getTags(params?: Parameters<typeof getInventoryTags>[2]): Observable<PaginatedResponseOfGetInventoryTagsResponse> {
    return getInventoryTags(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getTagsByIds(params: Parameters<typeof getInventoryTagsByIds>[2]): Observable<Array<GetInventoryTagsByIdsResponse>> {
    return getInventoryTagsByIds(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createTag(params: Parameters<typeof createInventoryTag>[2]): Observable<CreateInventoryTagResponse> {
    return createInventoryTag(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchTag(params: Parameters<typeof patchInventoryTag>[2]): Observable<PatchInventoryTagResponse> {
    return patchInventoryTag(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteTags(params: Parameters<typeof bulkDeleteInventoryTags>[2]): Observable<BulkDeleteInventoryTagsResponse> {
    return bulkDeleteInventoryTags(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
