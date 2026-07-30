import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getBackupTargets } from '@/api/generated/fn/admin/get-backup-targets';
import { getDocumentBackupState } from '@/api/generated/fn/admin/get-document-backup-state';
import { patchBackupTarget } from '@/api/generated/fn/admin/patch-backup-target';
import { createBackupSource } from '@/api/generated/fn/admin/create-backup-source';
import { patchBackupSource } from '@/api/generated/fn/admin/patch-backup-source';
import { deleteBackupSource } from '@/api/generated/fn/admin/delete-backup-source';
import { runBackupNow } from '@/api/generated/fn/admin/run-backup-now';
import { upsertBackupSchedule } from '@/api/generated/fn/admin/upsert-backup-schedule';
import { deleteBackupSchedule } from '@/api/generated/fn/admin/delete-backup-schedule';
import { connectBackupTarget } from '@/api/generated/fn/admin/connect-backup-target';
import { pollBackupTargetConnection } from '@/api/generated/fn/admin/poll-backup-target-connection';
import { confirmBackupTargetConnection } from '@/api/generated/fn/admin/confirm-backup-target-connection';
import { disconnectBackupTarget } from '@/api/generated/fn/admin/disconnect-backup-target';
import { validateBackupTargetConnection } from '@/api/generated/fn/admin/validate-backup-target-connection';
import type { GetBackupTargetsResponse } from '@/api/generated/models/get-backup-targets-response';
import type { GetDocumentBackupStateResponse } from '@/api/generated/models/get-document-backup-state-response';
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
import type { ConnectBackupTargetResponse } from '@/api/generated/models/connect-backup-target-response';
import type { PollBackupTargetConnectionResponse } from '@/api/generated/models/poll-backup-target-connection-response';
import type { ConfirmBackupTargetConnectionResponse } from '@/api/generated/models/confirm-backup-target-connection-response';
import type { ValidateBackupTargetConnectionResponse } from '@/api/generated/models/validate-backup-target-connection-response';

@Injectable({ providedIn: 'root' })
export class AdminBackupApiService {
  private http = inject(HttpClient);
  private config = inject(ApiConfiguration);

  listTargets(): Observable<GetBackupTargetsResponse> {
    return getBackupTargets(this.http, this.config.rootUrl).pipe(map(r => r.body));
  }

  /**
   * Where one document stands in every mirror configured to carry it. Read-only, admin-only, and 404s
   * for a document that does not exist or is in the trash.
   */
  getDocumentBackupState(documentId: string): Observable<GetDocumentBackupStateResponse> {
    return getDocumentBackupState(this.http, this.config.rootUrl, { documentId })
      .pipe(map(r => r.body));
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

  /** Starts a Google device authorization. The returned `pollToken` seals the device code. */
  connect(key: string): Observable<ConnectBackupTargetResponse> {
    return connectBackupTarget(this.http, this.config.rootUrl, { key }).pipe(map(r => r.body));
  }

  /** Asks Google whether the admin has consented yet. Poll at the interval `connect` returned. */
  pollConnection(key: string, pollToken: string): Observable<PollBackupTargetConnectionResponse> {
    return pollBackupTargetConnection(this.http, this.config.rootUrl, { key, body: { pollToken } })
      .pipe(map(r => r.body));
  }

  /**
   * Accepts a grant for a DIFFERENT Google account than the one on file. Destructive: it forgets every
   * recorded copy, because stored file ids mean nothing in another account's Drive.
   */
  confirmConnection(
    key: string, pendingToken: string): Observable<ConfirmBackupTargetConnectionResponse> {
    return confirmBackupTargetConnection(this.http, this.config.rootUrl, { key, body: { pendingToken } })
      .pipe(map(r => r.body));
  }

  disconnect(key: string): Observable<void> {
    return disconnectBackupTarget(this.http, this.config.rootUrl, { key }).pipe(map(() => void 0));
  }

  /** The one read-path call that actually touches Google — fired after render, never during it. */
  validate(key: string): Observable<ValidateBackupTargetConnectionResponse> {
    return validateBackupTargetConnection(this.http, this.config.rootUrl, { key }).pipe(map(r => r.body));
  }
}
