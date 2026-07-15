import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, NEVER } from 'rxjs';
import { StatementImportReviewPageComponent } from './statement-import-review-page.component';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';

const RESPONSE: StatementImportResponse = {
  jobId: 'job1',
  status: 'Completed',
  accountId: 'acc1',
  documentId: 'doc1',
  documentVersionId: 'ver1',
  header: {
    kind: 'Bank',
    institution: 'Test Bank',
    accountNumberMasked: '••••1234',
    currency: 'CAD',
    periodStart: '2026-06-01',
    periodEnd: '2026-06-30',
    openingBalance: 1000,
    closingBalance: 1500,
  },
  proposedBalanceEntry: {
    assertedAmount: 1500,
    date: '2026-06-30',
    sourceAnchor: 'New Balance',
    accepted: true,
  },
  accountNumberMatch: 'Mismatch',
  duplicateAssertionExists: false,
};

/* eslint-disable @typescript-eslint/no-explicit-any */
class StubStatementApi {
  response: StatementImportResponse = RESPONSE;
  lastCommit: any = null;
  get = () => of(this.response);
  commit = (params: any) => {
    this.lastCommit = params;
    return NEVER; // never resolves → toast/navigate success path is not reached
  };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

describe('StatementImportReviewPageComponent', () => {
  let api: StubStatementApi;

  function configure(): void {
    api = new StubStatementApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesStatementApiService, useValue: api },
        { provide: DocumentApiService, useValue: { getDocumentFile: () => of(null) } },
        { provide: HlmSidebarService, useValue: { isMobile: signal(false) } },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'acc1', jobId: 'job1' })) },
        },
      ],
    });
  }

  it('loads staged data and exposes the mis-binding verdict that drives the badge', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.data()?.status).toBe('Completed');
    expect(c.data()?.accountNumberMatch).toBe('Mismatch');
    expect(c.amount()).toBe('1500');
    expect(c.date()).toBe('2026-06-30');
    expect(c.canCommit()).toBe(true);
  });

  it('renders the mismatch badge in the DOM', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain("doesn't match this account");
  });

  it('commit() calls the API with the edited amount, date, and accepted=true', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.memo.set('June statement');
    c.commit();

    expect(api.lastCommit).not.toBeNull();
    expect(api.lastCommit.jobId).toBe('job1');
    expect(api.lastCommit.body).toEqual({
      assertedAmount: 1500,
      date: '2026-06-30',
      memo: 'June statement',
      accepted: true,
    });
  });
});
