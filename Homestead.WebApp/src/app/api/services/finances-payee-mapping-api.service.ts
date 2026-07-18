import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getPayeeMappings } from '@/api/generated/fn/finances/get-payee-mappings';
import { deletePayeeMapping } from '@/api/generated/fn/finances/delete-payee-mapping';
import type { PayeeMappingResponse } from '@/api/generated/models/payee-mapping-response';

export type { GetPayeeMappings$Params } from '@/api/generated/fn/finances/get-payee-mappings';
export type { DeletePayeeMapping$Params } from '@/api/generated/fn/finances/delete-payee-mapping';

/** Learned raw-description → payee mappings: list a payee's mappings and prune a wrong/stale one. */
@Injectable({ providedIn: 'root' })
export class FinancesPayeeMappingApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getPayeeMappings(params: Parameters<typeof getPayeeMappings>[2]): Observable<Array<PayeeMappingResponse>> {
    return getPayeeMappings(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deletePayeeMapping(params: Parameters<typeof deletePayeeMapping>[2]): Observable<void> {
    return deletePayeeMapping(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
