import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getItems } from '@/api/generated/fn/inventory/get-items';
import { getItem } from '@/api/generated/fn/inventory/get-item';
import { getItemActivity } from '@/api/generated/fn/inventory/get-item-activity';
import { createItem } from '@/api/generated/fn/inventory/create-item';
import { patchItem } from '@/api/generated/fn/inventory/patch-item';
import { deleteItem } from '@/api/generated/fn/inventory/delete-item';
import type { PaginatedResponseOfItemResponse } from '@/api/generated/models/paginated-response-of-item-response';
import type { PaginatedResponseOfGetItemActivityResponse } from '@/api/generated/models/paginated-response-of-get-item-activity-response';
import type { ItemResponse } from '@/api/generated/models/item-response';

export type { GetItems$Params } from '@/api/generated/fn/inventory/get-items';
export type { GetItem$Params } from '@/api/generated/fn/inventory/get-item';
export type { GetItemActivity$Params } from '@/api/generated/fn/inventory/get-item-activity';
export type { CreateItem$Params } from '@/api/generated/fn/inventory/create-item';
export type { PatchItem$Params } from '@/api/generated/fn/inventory/patch-item';
export type { DeleteItem$Params } from '@/api/generated/fn/inventory/delete-item';

@Injectable({ providedIn: 'root' })
export class InventoryItemApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getItems(params: Parameters<typeof getItems>[2]): Observable<PaginatedResponseOfItemResponse> {
    return getItems(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getItem(params: Parameters<typeof getItem>[2]): Observable<ItemResponse> {
    return getItem(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getItemActivity(
    params: Parameters<typeof getItemActivity>[2],
  ): Observable<PaginatedResponseOfGetItemActivityResponse> {
    return getItemActivity(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createItem(params: Parameters<typeof createItem>[2]): Observable<ItemResponse> {
    return createItem(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchItem(params: Parameters<typeof patchItem>[2]): Observable<ItemResponse> {
    return patchItem(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteItem(params: Parameters<typeof deleteItem>[2]): Observable<void> {
    return deleteItem(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
