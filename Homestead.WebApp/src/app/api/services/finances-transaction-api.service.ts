import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getAccountTransactions } from '@/api/generated/fn/finances/get-account-transactions';
import { getTransaction } from '@/api/generated/fn/finances/get-transaction';
import { deleteTransaction } from '@/api/generated/fn/finances/delete-transaction';
import { bulkDeleteTransactions } from '@/api/generated/fn/finances/bulk-delete-transactions';
import { createTransfer } from '@/api/generated/fn/finances/create-transfer';
import { patchTransfer } from '@/api/generated/fn/finances/patch-transfer';
import { createTrade } from '@/api/generated/fn/finances/create-trade';
import { patchTrade } from '@/api/generated/fn/finances/patch-trade';
import { createDividend } from '@/api/generated/fn/finances/create-dividend';
import { patchDividend } from '@/api/generated/fn/finances/patch-dividend';
import { createFlow } from '@/api/generated/fn/finances/create-flow';
import { patchFlow } from '@/api/generated/fn/finances/patch-flow';
import { createDrip } from '@/api/generated/fn/finances/create-drip';
import { patchDrip } from '@/api/generated/fn/finances/patch-drip';
import { deleteDrip } from '@/api/generated/fn/finances/delete-drip';
import { createBalanceEntry } from '@/api/generated/fn/finances/create-balance-entry';
import { patchBalanceEntry } from '@/api/generated/fn/finances/patch-balance-entry';
import { getBalanceEntry } from '@/api/generated/fn/finances/get-balance-entry';
import { getBalanceEntries } from '@/api/generated/fn/finances/get-balance-entries';
import { createOpeningBalance } from '@/api/generated/fn/finances/create-opening-balance';
import { patchOpeningBalance } from '@/api/generated/fn/finances/patch-opening-balance';
import { deleteOpeningBalance } from '@/api/generated/fn/finances/delete-opening-balance';
import { getOpeningBalance } from '@/api/generated/fn/finances/get-opening-balance';
import { getOpeningBalances } from '@/api/generated/fn/finances/get-opening-balances';
import type { BulkDeleteTransactionsResponse } from '@/api/generated/models/bulk-delete-transactions-response';
import type { PaginatedResponseOfTransactionResponse } from '@/api/generated/models/paginated-response-of-transaction-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { BalanceEntryResponse } from '@/api/generated/models/balance-entry-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';

export type { GetAccountTransactions$Params } from '@/api/generated/fn/finances/get-account-transactions';
export type { GetTransaction$Params } from '@/api/generated/fn/finances/get-transaction';
export type { DeleteTransaction$Params } from '@/api/generated/fn/finances/delete-transaction';
export type { BulkDeleteTransactions$Params } from '@/api/generated/fn/finances/bulk-delete-transactions';
export type { CreateTransfer$Params } from '@/api/generated/fn/finances/create-transfer';
export type { PatchTransfer$Params } from '@/api/generated/fn/finances/patch-transfer';
export type { CreateTrade$Params } from '@/api/generated/fn/finances/create-trade';
export type { PatchTrade$Params } from '@/api/generated/fn/finances/patch-trade';
export type { CreateDividend$Params } from '@/api/generated/fn/finances/create-dividend';
export type { PatchDividend$Params } from '@/api/generated/fn/finances/patch-dividend';
export type { CreateFlow$Params } from '@/api/generated/fn/finances/create-flow';
export type { PatchFlow$Params } from '@/api/generated/fn/finances/patch-flow';
export type { CreateDrip$Params } from '@/api/generated/fn/finances/create-drip';
export type { PatchDrip$Params } from '@/api/generated/fn/finances/patch-drip';
export type { DeleteDrip$Params } from '@/api/generated/fn/finances/delete-drip';
export type { CreateBalanceEntry$Params } from '@/api/generated/fn/finances/create-balance-entry';
export type { PatchBalanceEntry$Params } from '@/api/generated/fn/finances/patch-balance-entry';
export type { GetBalanceEntry$Params } from '@/api/generated/fn/finances/get-balance-entry';
export type { GetBalanceEntries$Params } from '@/api/generated/fn/finances/get-balance-entries';
export type { CreateOpeningBalance$Params } from '@/api/generated/fn/finances/create-opening-balance';
export type { PatchOpeningBalance$Params } from '@/api/generated/fn/finances/patch-opening-balance';
export type { DeleteOpeningBalance$Params } from '@/api/generated/fn/finances/delete-opening-balance';
export type { GetOpeningBalance$Params } from '@/api/generated/fn/finances/get-opening-balance';
export type { GetOpeningBalances$Params } from '@/api/generated/fn/finances/get-opening-balances';

@Injectable({ providedIn: 'root' })
export class FinancesTransactionApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  getAccountTransactions(params: Parameters<typeof getAccountTransactions>[2]): Observable<PaginatedResponseOfTransactionResponse> {
    return getAccountTransactions(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getTransaction(params: Parameters<typeof getTransaction>[2]): Observable<TransactionResponse> {
    return getTransaction(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteTransaction(params: Parameters<typeof deleteTransaction>[2]): Observable<void> {
    return deleteTransaction(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  bulkDeleteTransactions(params: Parameters<typeof bulkDeleteTransactions>[2]): Observable<BulkDeleteTransactionsResponse> {
    return bulkDeleteTransactions(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createTransfer(params: Parameters<typeof createTransfer>[2]): Observable<TransactionResponse> {
    return createTransfer(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchTransfer(params: Parameters<typeof patchTransfer>[2]): Observable<TransactionResponse> {
    return patchTransfer(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createTrade(params: Parameters<typeof createTrade>[2]): Observable<TransactionResponse> {
    return createTrade(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchTrade(params: Parameters<typeof patchTrade>[2]): Observable<TransactionResponse> {
    return patchTrade(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createDividend(params: Parameters<typeof createDividend>[2]): Observable<TransactionResponse> {
    return createDividend(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchDividend(params: Parameters<typeof patchDividend>[2]): Observable<TransactionResponse> {
    return patchDividend(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createFlow(params: Parameters<typeof createFlow>[2]): Observable<TransactionResponse> {
    return createFlow(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchFlow(params: Parameters<typeof patchFlow>[2]): Observable<TransactionResponse> {
    return patchFlow(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createDrip(params: Parameters<typeof createDrip>[2]): Observable<TransactionResponse> {
    return createDrip(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchDrip(params: Parameters<typeof patchDrip>[2]): Observable<TransactionResponse> {
    return patchDrip(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteDrip(params: Parameters<typeof deleteDrip>[2]): Observable<void> {
    return deleteDrip(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createBalanceEntry(params: Parameters<typeof createBalanceEntry>[2]): Observable<BalanceEntryResponse> {
    return createBalanceEntry(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchBalanceEntry(params: Parameters<typeof patchBalanceEntry>[2]): Observable<BalanceEntryResponse> {
    return patchBalanceEntry(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getBalanceEntry(params: Parameters<typeof getBalanceEntry>[2]): Observable<BalanceEntryResponse> {
    return getBalanceEntry(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getBalanceEntries(params: Parameters<typeof getBalanceEntries>[2]): Observable<Array<BalanceEntryResponse>> {
    return getBalanceEntries(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  createOpeningBalance(params: Parameters<typeof createOpeningBalance>[2]): Observable<OpeningBalanceResponse> {
    return createOpeningBalance(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  patchOpeningBalance(params: Parameters<typeof patchOpeningBalance>[2]): Observable<OpeningBalanceResponse> {
    return patchOpeningBalance(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  deleteOpeningBalance(params: Parameters<typeof deleteOpeningBalance>[2]): Observable<void> {
    return deleteOpeningBalance(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getOpeningBalance(params: Parameters<typeof getOpeningBalance>[2]): Observable<OpeningBalanceResponse> {
    return getOpeningBalance(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  getOpeningBalances(params: Parameters<typeof getOpeningBalances>[2]): Observable<Array<OpeningBalanceResponse>> {
    return getOpeningBalances(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
