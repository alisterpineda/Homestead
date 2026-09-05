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
import { getItemWarranties } from '@/api/generated/fn/inventory/get-item-warranties';
import { createItemWarranty } from '@/api/generated/fn/inventory/create-item-warranty';
import { updateItemWarranty } from '@/api/generated/fn/inventory/update-item-warranty';
import { deleteItemWarranty } from '@/api/generated/fn/inventory/delete-item-warranty';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';
import type { PaginatedResponseOfGetItemActivityResponse } from '@/api/generated/models/paginated-response-of-get-item-activity-response';
import type { ItemNoteResponse } from '@/api/generated/models/item-note-response';
import type { ItemWarrantyResponse } from '@/api/generated/models/item-warranty-response';

export type { GetItem$Params } from '@/api/generated/fn/inventory/get-item';
export type { GetItemActivity$Params } from '@/api/generated/fn/inventory/get-item-activity';
export type { CreateItemNote$Params } from '@/api/generated/fn/inventory/create-item-note';
export type { UpdateItemNote$Params } from '@/api/generated/fn/inventory/update-item-note';
export type { DeleteItemNote$Params } from '@/api/generated/fn/inventory/delete-item-note';
export type { GetItemWarranties$Params } from '@/api/generated/fn/inventory/get-item-warranties';
export type { CreateItemWarranty$Params } from '@/api/generated/fn/inventory/create-item-warranty';
export type { UpdateItemWarranty$Params } from '@/api/generated/fn/inventory/update-item-warranty';
export type { DeleteItemWarranty$Params } from '@/api/generated/fn/inventory/delete-item-warranty';

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

  getItemWarranties(
    params: Parameters<typeof getItemWarranties>[2],
  ): Observable<Array<ItemWarrantyResponse>> {
    return getItemWarranties(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  createItemWarranty(
    params: Parameters<typeof createItemWarranty>[2],
  ): Observable<ItemWarrantyResponse> {
    return createItemWarranty(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  updateItemWarranty(
    params: Parameters<typeof updateItemWarranty>[2],
  ): Observable<ItemWarrantyResponse> {
    return updateItemWarranty(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  deleteItemWarranty(params: Parameters<typeof deleteItemWarranty>[2]): Observable<void> {
    return deleteItemWarranty(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }
}
