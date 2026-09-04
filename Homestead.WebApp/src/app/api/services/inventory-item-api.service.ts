import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getItem } from '@/api/generated/fn/inventory/get-item';
import { getItemActivity } from '@/api/generated/fn/inventory/get-item-activity';
import { createItemNote } from '@/api/generated/fn/inventory/create-item-note';
import { updateItemNote } from '@/api/generated/fn/inventory/update-item-note';
import { deleteItemNote } from '@/api/generated/fn/inventory/delete-item-note';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';
import type { PaginatedResponseOfGetItemActivityResponse } from '@/api/generated/models/paginated-response-of-get-item-activity-response';
import type { ItemNoteResponse } from '@/api/generated/models/item-note-response';

export type { GetItem$Params } from '@/api/generated/fn/inventory/get-item';
export type { GetItemActivity$Params } from '@/api/generated/fn/inventory/get-item-activity';
export type { CreateItemNote$Params } from '@/api/generated/fn/inventory/create-item-note';
export type { UpdateItemNote$Params } from '@/api/generated/fn/inventory/update-item-note';
export type { DeleteItemNote$Params } from '@/api/generated/fn/inventory/delete-item-note';

@Injectable({ providedIn: 'root' })
export class InventoryItemApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getItem(params: Parameters<typeof getItem>[2]): Observable<ItemDetailResponse> {
    return getItem(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  getItemActivity(
    params: Parameters<typeof getItemActivity>[2],
  ): Observable<PaginatedResponseOfGetItemActivityResponse> {
    return getItemActivity(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  createItemNote(params: Parameters<typeof createItemNote>[2]): Observable<ItemNoteResponse> {
    return createItemNote(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  updateItemNote(params: Parameters<typeof updateItemNote>[2]): Observable<ItemNoteResponse> {
    return updateItemNote(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  deleteItemNote(params: Parameters<typeof deleteItemNote>[2]): Observable<void> {
    return deleteItemNote(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }
}
