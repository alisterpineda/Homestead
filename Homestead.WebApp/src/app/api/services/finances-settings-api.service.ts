import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getFinanceSettings } from '@/api/generated/fn/finances/get-finance-settings';
import { patchFinanceSettings } from '@/api/generated/fn/finances/patch-finance-settings';
import type { FinanceSettingsResponse } from '@/api/generated/models/finance-settings-response';

export type { PatchFinanceSettings$Params } from '@/api/generated/fn/finances/patch-finance-settings';

@Injectable({ providedIn: 'root' })
export class FinancesSettingsApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getFinanceSettings(): Observable<FinanceSettingsResponse> {
    return getFinanceSettings(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  patchFinanceSettings(params: Parameters<typeof patchFinanceSettings>[2]): Observable<FinanceSettingsResponse> {
    return patchFinanceSettings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
