import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getConsumables } from '@/api/generated/fn/inventory/get-consumables';
import { getConsumable } from '@/api/generated/fn/inventory/get-consumable';
import { getConsumableMovements } from '@/api/generated/fn/inventory/get-consumable-movements';
import { createConsumable } from '@/api/generated/fn/inventory/create-consumable';
import { patchConsumable } from '@/api/generated/fn/inventory/patch-consumable';
import { deleteConsumable } from '@/api/generated/fn/inventory/delete-consumable';
import { receiveStock } from '@/api/generated/fn/inventory/receive-stock';
import { consumeStock } from '@/api/generated/fn/inventory/consume-stock';
import { transferStock } from '@/api/generated/fn/inventory/transfer-stock';
import { adjustStock } from '@/api/generated/fn/inventory/adjust-stock';
import { trackDurableIndividually } from '@/api/generated/fn/inventory/track-durable-individually';
import { collapseDurables } from '@/api/generated/fn/inventory/collapse-durables';
import type { PaginatedResponseOfConsumableResponse } from '@/api/generated/models/paginated-response-of-consumable-response';
import type { PaginatedResponseOfStockMovementResponse } from '@/api/generated/models/paginated-response-of-stock-movement-response';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { TrackDurableResponse } from '@/api/generated/models/track-durable-response';

export type { GetConsumables$Params } from '@/api/generated/fn/inventory/get-consumables';
export type { GetConsumable$Params } from '@/api/generated/fn/inventory/get-consumable';
export type { GetConsumableMovements$Params } from '@/api/generated/fn/inventory/get-consumable-movements';
export type { CreateConsumable$Params } from '@/api/generated/fn/inventory/create-consumable';
export type { PatchConsumable$Params } from '@/api/generated/fn/inventory/patch-consumable';
export type { DeleteConsumable$Params } from '@/api/generated/fn/inventory/delete-consumable';
export type { ReceiveStock$Params } from '@/api/generated/fn/inventory/receive-stock';
export type { ConsumeStock$Params } from '@/api/generated/fn/inventory/consume-stock';
export type { TransferStock$Params } from '@/api/generated/fn/inventory/transfer-stock';
export type { AdjustStock$Params } from '@/api/generated/fn/inventory/adjust-stock';
export type { TrackDurableIndividually$Params } from '@/api/generated/fn/inventory/track-durable-individually';
export type { CollapseDurables$Params } from '@/api/generated/fn/inventory/collapse-durables';

@Injectable({ providedIn: 'root' })
export class InventoryConsumableApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getConsumables(params: Parameters<typeof getConsumables>[2]): Observable<PaginatedResponseOfConsumableResponse> {
    return getConsumables(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  getConsumable(params: Parameters<typeof getConsumable>[2]): Observable<ConsumableDetailResponse> {
    return getConsumable(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  getConsumableMovements(
    params: Parameters<typeof getConsumableMovements>[2],
  ): Observable<PaginatedResponseOfStockMovementResponse> {
    return getConsumableMovements(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  createConsumable(params: Parameters<typeof createConsumable>[2]): Observable<ConsumableResponse> {
    return createConsumable(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  patchConsumable(params: Parameters<typeof patchConsumable>[2]): Observable<ConsumableResponse> {
    return patchConsumable(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  deleteConsumable(params: Parameters<typeof deleteConsumable>[2]): Observable<void> {
    return deleteConsumable(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  receiveStock(params: Parameters<typeof receiveStock>[2]): Observable<ConsumableDetailResponse> {
    return receiveStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  consumeStock(params: Parameters<typeof consumeStock>[2]): Observable<ConsumableDetailResponse> {
    return consumeStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  transferStock(params: Parameters<typeof transferStock>[2]): Observable<ConsumableDetailResponse> {
    return transferStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  adjustStock(params: Parameters<typeof adjustStock>[2]): Observable<ConsumableDetailResponse> {
    return adjustStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  trackDurable(params: Parameters<typeof trackDurableIndividually>[2]): Observable<TrackDurableResponse> {
    return trackDurableIndividually(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  collapseDurables(params: Parameters<typeof collapseDurables>[2]): Observable<ConsumableDetailResponse> {
    return collapseDurables(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }
}
