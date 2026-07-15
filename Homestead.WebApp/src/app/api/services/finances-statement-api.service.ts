import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { initiateStatementImport } from '@/api/generated/fn/finances/initiate-statement-import';
import { getStatementImport } from '@/api/generated/fn/finances/get-statement-import';
import { commitStatementImport } from '@/api/generated/fn/finances/commit-statement-import';
import { rollbackStatementImport } from '@/api/generated/fn/finances/rollback-statement-import';
import type { InitiateStatementImportResponse } from '@/api/generated/models/initiate-statement-import-response';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';
import type { CommitStatementImportResponse } from '@/api/generated/models/commit-statement-import-response';
import type { RollbackStatementImportResponse } from '@/api/generated/models/rollback-statement-import-response';

export type { InitiateStatementImport$Params } from '@/api/generated/fn/finances/initiate-statement-import';
export type { GetStatementImport$Params } from '@/api/generated/fn/finances/get-statement-import';
export type { CommitStatementImport$Params } from '@/api/generated/fn/finances/commit-statement-import';
export type { RollbackStatementImport$Params } from '@/api/generated/fn/finances/rollback-statement-import';

@Injectable({ providedIn: 'root' })
export class FinancesStatementApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  initiate(
    params: Parameters<typeof initiateStatementImport>[2],
  ): Observable<InitiateStatementImportResponse> {
    return initiateStatementImport(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  get(params: Parameters<typeof getStatementImport>[2]): Observable<StatementImportResponse> {
    return getStatementImport(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  commit(
    params: Parameters<typeof commitStatementImport>[2],
  ): Observable<CommitStatementImportResponse> {
    return commitStatementImport(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }

  rollback(
    params: Parameters<typeof rollbackStatementImport>[2],
  ): Observable<RollbackStatementImportResponse> {
    return rollbackStatementImport(this.http, this.config.rootUrl, params).pipe(map(r => r.body));
  }
}
