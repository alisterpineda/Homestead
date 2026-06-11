import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getSupplies } from '@/api/generated/fn/inventory/get-supplies';
import { getSupply } from '@/api/generated/fn/inventory/get-supply';
import { getSupplyMovements } from '@/api/generated/fn/inventory/get-supply-movements';
import { createSupply } from '@/api/generated/fn/inventory/create-supply';
import { patchSupply } from '@/api/generated/fn/inventory/patch-supply';
import { deleteSupply } from '@/api/generated/fn/inventory/delete-supply';
import { receiveStock } from '@/api/generated/fn/inventory/receive-stock';
import { consumeStock } from '@/api/generated/fn/inventory/consume-stock';
import { transferStock } from '@/api/generated/fn/inventory/transfer-stock';
import { adjustStock } from '@/api/generated/fn/inventory/adjust-stock';
import type { PaginatedResponseOfSupplyResponse } from '@/api/generated/models/paginated-response-of-supply-response';
import type { PaginatedResponseOfStockMovementResponse } from '@/api/generated/models/paginated-response-of-stock-movement-response';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { SupplyDetailResponse } from '@/api/generated/models/supply-detail-response';

export type { GetSupplies$Params } from '@/api/generated/fn/inventory/get-supplies';
export type { GetSupply$Params } from '@/api/generated/fn/inventory/get-supply';
export type { GetSupplyMovements$Params } from '@/api/generated/fn/inventory/get-supply-movements';
export type { CreateSupply$Params } from '@/api/generated/fn/inventory/create-supply';
export type { PatchSupply$Params } from '@/api/generated/fn/inventory/patch-supply';
export type { DeleteSupply$Params } from '@/api/generated/fn/inventory/delete-supply';
export type { ReceiveStock$Params } from '@/api/generated/fn/inventory/receive-stock';
export type { ConsumeStock$Params } from '@/api/generated/fn/inventory/consume-stock';
export type { TransferStock$Params } from '@/api/generated/fn/inventory/transfer-stock';
export type { AdjustStock$Params } from '@/api/generated/fn/inventory/adjust-stock';

@Injectable({ providedIn: 'root' })
export class InventorySupplyApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getSupplies(params: Parameters<typeof getSupplies>[2]): Observable<PaginatedResponseOfSupplyResponse> {
    return getSupplies(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  getSupply(params: Parameters<typeof getSupply>[2]): Observable<SupplyDetailResponse> {
    return getSupply(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  getSupplyMovements(
    params: Parameters<typeof getSupplyMovements>[2],
  ): Observable<PaginatedResponseOfStockMovementResponse> {
    return getSupplyMovements(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  createSupply(params: Parameters<typeof createSupply>[2]): Observable<SupplyResponse> {
    return createSupply(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  patchSupply(params: Parameters<typeof patchSupply>[2]): Observable<SupplyResponse> {
    return patchSupply(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  deleteSupply(params: Parameters<typeof deleteSupply>[2]): Observable<void> {
    return deleteSupply(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  receiveStock(params: Parameters<typeof receiveStock>[2]): Observable<SupplyDetailResponse> {
    return receiveStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  consumeStock(params: Parameters<typeof consumeStock>[2]): Observable<SupplyDetailResponse> {
    return consumeStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  transferStock(params: Parameters<typeof transferStock>[2]): Observable<SupplyDetailResponse> {
    return transferStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }

  adjustStock(params: Parameters<typeof adjustStock>[2]): Observable<SupplyDetailResponse> {
    return adjustStock(this.http, this.config.rootUrl, params).pipe(map((r) => r.body));
  }
}
