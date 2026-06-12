import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getInventoryOverview } from '@/api/generated/fn/inventory/get-inventory-overview';
import type { GetInventoryOverviewResponse } from '@/api/generated/models/get-inventory-overview-response';

@Injectable({ providedIn: 'root' })
export class InventoryOverviewApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getOverview(): Observable<GetInventoryOverviewResponse> {
    return getInventoryOverview(this.http, this.config.rootUrl, {}).pipe(map(r => r.body));
  }
}
