import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getPayees } from '@/api/generated/fn/finances/get-payees';
import { createPayee } from '@/api/generated/fn/finances/create-payee';
import { patchPayee } from '@/api/generated/fn/finances/patch-payee';
import { deletePayee } from '@/api/generated/fn/finances/delete-payee';
import type { PaginatedResponseOfPayeeResponse } from '@/api/generated/models/paginated-response-of-payee-response';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

export type { GetPayees$Params } from '@/api/generated/fn/finances/get-payees';
export type { CreatePayee$Params } from '@/api/generated/fn/finances/create-payee';
export type { PatchPayee$Params } from '@/api/generated/fn/finances/patch-payee';
export type { DeletePayee$Params } from '@/api/generated/fn/finances/delete-payee';

@Injectable({ providedIn: 'root' })
export class FinancesPayeeApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getPayees(params: Parameters<typeof getPayees>[2]): Observable<PaginatedResponseOfPayeeResponse> {
    return getPayees(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createPayee(params: Parameters<typeof createPayee>[2]): Observable<PayeeResponse> {
    return createPayee(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchPayee(params: Parameters<typeof patchPayee>[2]): Observable<PayeeResponse> {
    return patchPayee(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deletePayee(params: Parameters<typeof deletePayee>[2]): Observable<void> {
    return deletePayee(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
