import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getAccounts } from '@/api/generated/fn/finances/get-accounts';
import { getAccount } from '@/api/generated/fn/finances/get-account';
import { createAccount } from '@/api/generated/fn/finances/create-account';
import { patchAccount } from '@/api/generated/fn/finances/patch-account';
import { deleteAccount } from '@/api/generated/fn/finances/delete-account';
import { getAccountPositions } from '@/api/generated/fn/finances/get-account-positions';
import type { PaginatedResponseOfAccountResponse } from '@/api/generated/models/paginated-response-of-account-response';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { PositionResponse } from '@/api/generated/models/position-response';
import { localEndOfTodayIso } from '@/modules/finances/shared/date';

export type { GetAccounts$Params } from '@/api/generated/fn/finances/get-accounts';
export type { GetAccount$Params } from '@/api/generated/fn/finances/get-account';
export type { CreateAccount$Params } from '@/api/generated/fn/finances/create-account';
export type { PatchAccount$Params } from '@/api/generated/fn/finances/patch-account';
export type { DeleteAccount$Params } from '@/api/generated/fn/finances/delete-account';
export type { GetAccountPositions$Params } from '@/api/generated/fn/finances/get-account-positions';

@Injectable({ providedIn: 'root' })
export class FinancesAccountApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getAccounts(params: Parameters<typeof getAccounts>[2]): Observable<PaginatedResponseOfAccountResponse> {
    const withAsOf = { AsOf: localEndOfTodayIso(), ...params };
    return getAccounts(this.http, this.config.rootUrl, withAsOf).pipe(map(r => r.body));
  }

  getAccount(params: Parameters<typeof getAccount>[2]): Observable<AccountResponse> {
    const withAsOf = { AsOf: localEndOfTodayIso(), ...params };
    return getAccount(this.http, this.config.rootUrl, withAsOf).pipe(map(r => r.body));
  }

  createAccount(params: Parameters<typeof createAccount>[2]): Observable<AccountResponse> {
    return createAccount(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchAccount(params: Parameters<typeof patchAccount>[2]): Observable<AccountResponse> {
    return patchAccount(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteAccount(params: Parameters<typeof deleteAccount>[2]): Observable<void> {
    return deleteAccount(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getAccountPositions(params: Parameters<typeof getAccountPositions>[2]): Observable<Array<PositionResponse>> {
    return getAccountPositions(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
