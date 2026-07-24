import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getBackupTargets } from '@/api/generated/fn/admin/get-backup-targets';
import { patchBackupTarget } from '@/api/generated/fn/admin/patch-backup-target';
import { createBackupSource } from '@/api/generated/fn/admin/create-backup-source';
import { patchBackupSource } from '@/api/generated/fn/admin/patch-backup-source';
import { deleteBackupSource } from '@/api/generated/fn/admin/delete-backup-source';
import { runBackupNow } from '@/api/generated/fn/admin/run-backup-now';
import { upsertBackupSchedule } from '@/api/generated/fn/admin/upsert-backup-schedule';
import { deleteBackupSchedule } from '@/api/generated/fn/admin/delete-backup-schedule';
import type { GetBackupTargetsResponse } from '@/api/generated/models/get-backup-targets-response';
import type { PatchBackupTargetBody } from '@/api/generated/models/patch-backup-target-body';
import type { PatchBackupTargetResponse } from '@/api/generated/models/patch-backup-target-response';
import type { CreateBackupSourceBody } from '@/api/generated/models/create-backup-source-body';
import type { CreateBackupSourceResponse } from '@/api/generated/models/create-backup-source-response';
import type { PatchBackupSourceBody } from '@/api/generated/models/patch-backup-source-body';
import type { PatchBackupSourceResponse } from '@/api/generated/models/patch-backup-source-response';
import type { RunBackupNowBody } from '@/api/generated/models/run-backup-now-body';
import type { RunBackupNowResponse } from '@/api/generated/models/run-backup-now-response';
import type { UpsertBackupScheduleBody } from '@/api/generated/models/upsert-backup-schedule-body';
import type { UpsertBackupScheduleResponse } from '@/api/generated/models/upsert-backup-schedule-response';

@Injectable({ providedIn: 'root' })
export class AdminBackupApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  listTargets(): Observable<GetBackupTargetsResponse> {
    return getBackupTargets(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  /** Target-level enable only — the prune latch is acknowledged per source. */
  patchTarget(key: string, body: PatchBackupTargetBody): Observable<PatchBackupTargetResponse> {
    return patchBackupTarget(this.http, this.config.rootUrl, { key, body }).pipe(map(r => r.body));
  }

  createSource(body: CreateBackupSourceBody): Observable<CreateBackupSourceResponse> {
    return createBackupSource(this.http, this.config.rootUrl, { body }).pipe(map(r => r.body));
  }

  patchSource(id: string, body: PatchBackupSourceBody): Observable<PatchBackupSourceResponse> {
    return patchBackupSource(this.http, this.config.rootUrl, { id, body }).pipe(map(r => r.body));
  }

  /** Without `cancelRun`, a live run makes this a 409 — the caller then offers to cancel it. */
  deleteSource(id: string, cancelRun?: boolean): Observable<void> {
    return deleteBackupSource(this.http, this.config.rootUrl, { id, cancelRun }).pipe(map(() => void 0));
  }

  runNow(sourceId: string, body: RunBackupNowBody): Observable<RunBackupNowResponse> {
    return runBackupNow(this.http, this.config.rootUrl, { id: sourceId, body }).pipe(map(r => r.body));
  }

  upsertSchedule(sourceId: string, body: UpsertBackupScheduleBody): Observable<UpsertBackupScheduleResponse> {
    return upsertBackupSchedule(this.http, this.config.rootUrl, { id: sourceId, body })
      .pipe(map(r => r.body));
  }

  deleteSchedule(sourceId: string): Observable<void> {
    return deleteBackupSchedule(this.http, this.config.rootUrl, { id: sourceId }).pipe(map(() => void 0));
  }
}
