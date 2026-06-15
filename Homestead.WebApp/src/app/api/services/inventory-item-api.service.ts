import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getItem } from '@/api/generated/fn/inventory/get-item';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';

export type { GetItem$Params } from '@/api/generated/fn/inventory/get-item';

@Injectable({ providedIn: 'root' })
export class InventoryItemApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getItem(params: Parameters<typeof getItem>[2]): Observable<ItemDetailResponse> {
    return getItem(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }
}
