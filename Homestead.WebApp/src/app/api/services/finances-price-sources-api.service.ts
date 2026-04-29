import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getPriceSources } from '@/api/generated/fn/finances/get-price-sources';
import { getPriceSource } from '@/api/generated/fn/finances/get-price-source';
import { patchPriceSource } from '@/api/generated/fn/finances/patch-price-source';
import { upsertPriceSourceSchedule } from '@/api/generated/fn/finances/upsert-price-source-schedule';
import { deletePriceSourceSchedule } from '@/api/generated/fn/finances/delete-price-source-schedule';
import { runPriceSourceImportNow } from '@/api/generated/fn/finances/run-price-source-import-now';
import type { GetPriceSourcesResponse } from '@/api/generated/models/get-price-sources-response';
import type { GetPriceSourceResponse } from '@/api/generated/models/get-price-source-response';
import type { PatchPriceSourceBody } from '@/api/generated/models/patch-price-source-body';
import type { PatchPriceSourceResponse } from '@/api/generated/models/patch-price-source-response';
import type { UpsertPriceSourceScheduleBody } from '@/api/generated/models/upsert-price-source-schedule-body';
import type { UpsertPriceSourceScheduleResponse } from '@/api/generated/models/upsert-price-source-schedule-response';
import type { RunNowBody } from '@/api/generated/models/run-now-body';
import type { RunPriceSourceImportNowResponse } from '@/api/generated/models/run-price-source-import-now-response';
import type { CommodityPriceSource } from '@/api/generated/models/commodity-price-source';

@Injectable({ providedIn: 'root' })
export class FinancesPriceSourcesApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  list(): Observable<GetPriceSourcesResponse> {
    return getPriceSources(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  get(source: CommodityPriceSource): Observable<GetPriceSourceResponse> {
    return getPriceSource(this.http, this.config.rootUrl, { source }).pipe(map(r => r.body));
  }

  patch(source: CommodityPriceSource, body: PatchPriceSourceBody): Observable<PatchPriceSourceResponse> {
    return patchPriceSource(this.http, this.config.rootUrl, { source, body }).pipe(map(r => r.body));
  }

  upsertSchedule(
    source: CommodityPriceSource,
    body: UpsertPriceSourceScheduleBody,
  ): Observable<UpsertPriceSourceScheduleResponse> {
    return upsertPriceSourceSchedule(this.http, this.config.rootUrl, { source, body }).pipe(map(r => r.body));
  }

  deleteSchedule(source: CommodityPriceSource): Observable<void> {
    return deletePriceSourceSchedule(this.http, this.config.rootUrl, { source }).pipe(map(() => void 0));
  }

  runNow(source: CommodityPriceSource, body?: RunNowBody): Observable<RunPriceSourceImportNowResponse> {
    return runPriceSourceImportNow(this.http, this.config.rootUrl, { source, body: body ?? null }).pipe(map(r => r.body));
  }
}
