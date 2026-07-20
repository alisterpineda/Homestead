import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { StatementImportDialogComponent } from './statement-import-dialog.component';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';

// The "already imported" initiate rejection, verbatim to what InitiateStatementImportHandler tags.
const ALREADY_IMPORTED_ERR = new HttpErrorResponse({
  status: 400,
  error: {
    errorCode: 'statement_already_imported',
    detail: 'This statement has already been imported. Roll back the previous import to import it again.',
  },
});
// A different precondition failure (no extracted text) — a plain 400 with no errorCode.
const NO_TEXT_ERR = new HttpErrorResponse({
  status: 400,
  error: { detail: 'This document has no extracted text, so there is nothing to import.' },
});

/* eslint-disable @typescript-eslint/no-explicit-any */
class StubStatementApi {
  initiateCalls = 0;
  initiateError: unknown = null;
  rollbackCalls: any[] = [];
  rollbackShouldFail = false;
  // First call rejects with `initiateError` (the guard); a later call (post-rollback re-parse) succeeds.
  initiate = (_params: any) => {
    this.initiateCalls++;
    if (this.initiateCalls === 1 && this.initiateError) {
      return throwError(() => this.initiateError);
    }
    return of({ jobId: 'job2' });
  };
  rollback = (params: any) => {
    this.rollbackCalls.push(params);
    return this.rollbackShouldFail
      ? throwError(() => new HttpErrorResponse({ status: 404, error: { detail: 'No imported transactions found for this document.' } }))
      : of({ deletedCount: 3 });
  };
}
class StubDocumentApi {
  getDocument = (_params: any) =>
    of({ id: 'doc1', name: 'June statement', versions: [{ id: 'ver1', versionNumber: 1 }] } as any);
}
class StubHub {
  waitForJob = vi.fn().mockResolvedValue({ entityId: 'doc1' });
}
/* eslint-enable @typescript-eslint/no-explicit-any */

describe('StatementImportDialogComponent — re-import warning + rollback', () => {
  let component: StatementImportDialogComponent;
  let statementApi: StubStatementApi;
  let router: { navigate: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    statementApi = new StubStatementApi();
    router = { navigate: vi.fn().mockResolvedValue(true) };

    TestBed.configureTestingModule({
      imports: [StatementImportDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesStatementApiService, useValue: statementApi },
        { provide: DocumentApiService, useClass: StubDocumentApi },
        { provide: ProcessingHubService, useClass: StubHub },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    });

    const fixture = TestBed.createComponent(StatementImportDialogComponent);
    fixture.componentRef.setInput('accountId', 'acc1');
    component = fixture.componentInstance;
    // The inline alert only renders (vs. falling back to a toast) while the dialog is open.
    component.onStateChanged('open');
  });

  it('surfaces an already-imported rejection as a recoverable warning, not a hard error', async () => {
    statementApi.initiateError = ALREADY_IMPORTED_ERR;

    await (component as any).runParse('doc1', 'acc1');

    const err = component.importError();
    expect(err).not.toBeNull();
    expect(err!.alreadyImported).toBe(true);
    expect(err!.document).toEqual({ id: 'doc1', name: 'June statement' });
  });

  it('keeps every other 400 a hard error (no rollback affordance)', async () => {
    statementApi.initiateError = NO_TEXT_ERR;

    await (component as any).runParse('doc1', 'acc1');

    expect(component.importError()?.alreadyImported).toBe(false);
  });

  it('rolls back the prior import, then proceeds with the parse', async () => {
    statementApi.initiateError = ALREADY_IMPORTED_ERR;
    await (component as any).runParse('doc1', 'acc1');

    await component.rollbackPreviousImport();

    // Rollback keyed on the same document the warning names.
    expect(statementApi.rollbackCalls).toEqual([{ documentId: 'doc1' }]);
    // The blocked import then proceeds: initiate re-runs and navigation reaches the review route.
    expect(statementApi.initiateCalls).toBe(2);
    expect(router.navigate).toHaveBeenCalledWith([
      '/finances/accounts', 'acc1', 'statement-import', 'job2',
    ]);
    // Warning cleared, not lingering.
    expect(component.importError()).toBeNull();
    expect(component.rollingBack()).toBe(false);
  });

  it('leaves the warning in place and does not re-parse when rollback fails', async () => {
    statementApi.initiateError = ALREADY_IMPORTED_ERR;
    statementApi.rollbackShouldFail = true;
    await (component as any).runParse('doc1', 'acc1');

    await component.rollbackPreviousImport();

    expect(statementApi.initiateCalls).toBe(1); // no re-parse
    expect(component.importError()?.alreadyImported).toBe(true); // still actionable
    expect(component.rollingBack()).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
