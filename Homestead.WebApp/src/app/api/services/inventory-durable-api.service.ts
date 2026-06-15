import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDurables } from '@/api/generated/fn/inventory/get-durables';
import { getDurable } from '@/api/generated/fn/inventory/get-durable';
import { getDurableActivity } from '@/api/generated/fn/inventory/get-durable-activity';
import { createDurable } from '@/api/generated/fn/inventory/create-durable';
import { patchDurable } from '@/api/generated/fn/inventory/patch-durable';
import { deleteDurable } from '@/api/generated/fn/inventory/delete-durable';
import type { PaginatedResponseOfDurableResponse } from '@/api/generated/models/paginated-response-of-durable-response';
import type { PaginatedResponseOfGetDurableActivityResponse } from '@/api/generated/models/paginated-response-of-get-durable-activity-response';
import type { DurableResponse } from '@/api/generated/models/durable-response';

export type { GetDurables$Params } from '@/api/generated/fn/inventory/get-durables';
export type { GetDurable$Params } from '@/api/generated/fn/inventory/get-durable';
export type { GetDurableActivity$Params } from '@/api/generated/fn/inventory/get-durable-activity';
export type { CreateDurable$Params } from '@/api/generated/fn/inventory/create-durable';
export type { PatchDurable$Params } from '@/api/generated/fn/inventory/patch-durable';
export type { DeleteDurable$Params } from '@/api/generated/fn/inventory/delete-durable';

@Injectable({ providedIn: 'root' })
export class InventoryDurableApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getDurables(params: Parameters<typeof getDurables>[2]): Observable<PaginatedResponseOfDurableResponse> {
    return getDurables(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDurable(params: Parameters<typeof getDurable>[2]): Observable<DurableResponse> {
    return getDurable(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getDurableActivity(
    params: Parameters<typeof getDurableActivity>[2],
  ): Observable<PaginatedResponseOfGetDurableActivityResponse> {
    return getDurableActivity(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createDurable(params: Parameters<typeof createDurable>[2]): Observable<DurableResponse> {
    return createDurable(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchDurable(params: Parameters<typeof patchDurable>[2]): Observable<DurableResponse> {
    return patchDurable(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteDurable(params: Parameters<typeof deleteDurable>[2]): Observable<void> {
    return deleteDurable(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
