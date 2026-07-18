import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, NEVER } from 'rxjs';
import { StatementImportReviewPageComponent } from './statement-import-review-page.component';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { FinancesSettingsApiService } from '@/api/services/finances-settings-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

// The line-item table's hlm-select cells measure their trigger width via ResizeObserver, absent in jsdom.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

// Header opening 1000 → closing 1500. The two mappable lines (Expense 100, Income 600) net +500 on an asset
// account, so the default selection reconciles exactly (checksum 'balanced'). The third line is un-mappable.
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
  lineItems: [
    { index: 0, date: '2026-06-03', description: 'GROCERY', amount: 100, direction: 'Expense', runningBalance: null, mappable: true, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
    { index: 1, date: '2026-06-15', description: 'PAYROLL', amount: 600, direction: 'Income', runningBalance: null, mappable: true, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
    { index: 2, date: null, description: 'MYSTERY', amount: 0, direction: 'Expense', runningBalance: null, mappable: false, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
  ],
  accountNumberMatch: 'Mismatch',
  duplicateAssertionExists: false,
  textTruncated: false,
};

const ASSET_ACCOUNT: AccountResponse = {
  id: 'acc1', name: 'Chequing', accountType: 'Asset', kind: 'Cash', reportingCurrencyId: 'cad',
  createdAt: '2026-01-01', updatedAt: '2026-01-01', hasTransactions: true, isArchived: false, isSystem: false,
};
const LIABILITY_ACCOUNT: AccountResponse = { ...ASSET_ACCOUNT, accountType: 'Liability', kind: 'CreditCard' };
const CAD: CommodityResponse = {
  id: 'cad', symbol: 'CAD', name: 'Canadian Dollar', kind: 'Currency', displayPrecision: 2,
  canDelete: false, exchange: null, isSystemManaged: true, latestPriceAt: null, priceCount: 0,
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
class StubAccountApi {
  account: AccountResponse = ASSET_ACCOUNT;
  getAccount = () => of(this.account);
}
class StubPayeeApi {
  created: any[] = [];
  createPayee = (params: any) => {
    this.created.push(params);
    return of({
      id: `new-${params.body.name}`, name: params.body.name, isArchived: false,
      transactionCount: 0, createdAt: '', updatedAt: '',
    });
  };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

describe('StatementImportReviewPageComponent', () => {
  let api: StubStatementApi;
  let accountApi: StubAccountApi;
  let payeeApi: StubPayeeApi;

  function configure(response: StatementImportResponse = RESPONSE, account: AccountResponse = ASSET_ACCOUNT): void {
    api = new StubStatementApi();
    api.response = response;
    accountApi = new StubAccountApi();
    accountApi.account = account;
    payeeApi = new StubPayeeApi();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesStatementApiService, useValue: api },
        { provide: FinancesAccountApiService, useValue: accountApi },
        { provide: FinancesPayeeApiService, useValue: payeeApi },
        { provide: FinancesSettingsApiService, useValue: { getFinanceSettings: () => of({ autoCategoriseFromPayee: true }) } },
        { provide: FinancesCommodityApiService, useValue: { getCommodity: () => of(CAD) } },
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

  it('seeds line-item edit state, leaving un-mappable rows unchecked', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    const rows = c.lineItems();
    expect(rows.length).toBe(3);
    expect(c.isAccepted(0)).toBe(true);   // mappable, default-accepted
    expect(c.isAccepted(1)).toBe(true);
    expect(c.isAccepted(2)).toBe(false);  // un-mappable → seeded unchecked
    expect(c.rowCommittable(rows[2])).toBe(false);
    expect(c.acceptedLines().length).toBe(2);
  });

  it('renders a row per line item in the table', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Transactions (3)');
  });

  it('select-all toggles acceptance of all committable rows', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.allSelected()).toBe(true);

    c.toggleAll(false);
    expect(c.acceptedLines().length).toBe(0);
    expect(c.allSelected()).toBe(false);

    c.toggleAll(true);
    expect(c.acceptedLines().length).toBe(2);   // the un-mappable row stays out
    expect(c.isAccepted(2)).toBe(false);
  });

  it('checksum is balanced with the default selection on an asset account', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.checksum().state).toBe('balanced');
  });

  it('checksum goes off-by when a reconciling line is deselected', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.setRowAccepted(1, false); // drop the +600 income → expected 900 vs closing 1500
    const ck = c.checksum();
    expect(ck.state).toBe('off');
    expect(c.offAmount()).toContain('600');
  });

  it('checksum flips on a liability account (Out increases owed)', async () => {
    configure(RESPONSE, LIABILITY_ACCOUNT);
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Liability: Expense +100, Income −600 → 1000 − 500 = 500 vs closing 1500 → off.
    expect(c.checksum().state).toBe('off');
  });

  it('checksum is unavailable without both opening and closing balances', async () => {
    const noClosing: StatementImportResponse = {
      ...RESPONSE,
      header: { ...RESPONSE.header!, closingBalance: null },
    };
    configure(noClosing);
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.checksum().state).toBe('unavailable');
  });

  it('shows the truncation note when the statement text was clipped', async () => {
    configure({ ...RESPONSE, textTruncated: true });
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('may have been truncated');
  });

  it('commit() sends the balance entry and the accepted, edited line items', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.memo.set('June statement');
    c.commit();

    expect(api.lastCommit).not.toBeNull();
    expect(api.lastCommit.jobId).toBe('job1');
    expect(api.lastCommit.body).toEqual({
      accepted: true,
      assertedAmount: 1500,
      date: '2026-06-30',
      memo: 'June statement',
      lineItems: [
        { index: 0, direction: 'Expense', amount: 100, date: '2026-06-03', description: 'GROCERY', payeeId: null, learn: false },
        { index: 1, direction: 'Income', amount: 600, date: '2026-06-15', description: 'PAYROLL', payeeId: null, learn: false },
      ],
    });
  });

  it('commit() carries a per-line payee pick through to the request', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Pick a payee on the first accepted line (index 0).
    c.onRowPayeeChange(0, {
      id: 'payee-costco', name: 'Costco', isArchived: false, transactionCount: 0,
      createdAt: '', updatedAt: '',
    });
    c.commit();

    expect(api.lastCommit.body.lineItems[0]).toEqual(
      expect.objectContaining({ description: 'GROCERY', payeeId: 'payee-costco' }));
    expect(api.lastCommit.body.lineItems[1].payeeId).toBeNull();
  });

  it('bulk-sets direction across the current selection, skipping unselected rows', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Default selection = the two mappable rows (0, 1). Flip both to Income.
    c.bulkSetDirection('Income');
    const rows = c.lineItems();
    expect(rows[0].direction).toBe('Income');
    expect(rows[1].direction).toBe('Income');
    expect(rows[2].direction).toBe('Expense'); // un-mappable + unselected → untouched
  });

  it('bulk-assigns then clears the payee across the selection', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.bulkAssignPayee({
      id: 'payee-hydro', name: 'Hydro', isArchived: false, transactionCount: 0,
      createdAt: '', updatedAt: '',
    });
    expect(c.lineItems()[0].payee).toEqual({ id: 'payee-hydro', name: 'Hydro' });
    expect(c.lineItems()[1].payee).toEqual({ id: 'payee-hydro', name: 'Hydro' });
    expect(c.lineItems()[2].payee).toBeNull(); // unselected

    c.bulkClearPayee();
    expect(c.lineItems()[0].payee).toBeNull();
    expect(c.lineItems()[1].payee).toBeNull();
  });

  it('filters visible rows while hidden-but-selected rows still commit', async () => {
    const withDup: StatementImportResponse = {
      ...RESPONSE,
      lineItems: [
        RESPONSE.lineItems![0],
        { ...RESPONSE.lineItems![1], duplicateSuspected: true },
        RESPONSE.lineItems![2],
      ],
    };
    configure(withDup);
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.visibleLineItems().length).toBe(3);
    expect(c.acceptedLines().length).toBe(2);

    // Filter to duplicates → only row 1 visible, but row 0 stays selected and commit-bound.
    c.filterMode.set('duplicates');
    expect(c.visibleLineItems().map((r) => r.index)).toEqual([1]);
    expect(c.acceptedLines().map((r) => r.index)).toEqual([0, 1]);

    // 'unmappable' → only the un-mappable row 2.
    c.filterMode.set('unmappable');
    expect(c.visibleLineItems().map((r) => r.index)).toEqual([2]);
  });

  // ─── Payee suggestions (learned / ghost / learn flag / warning dialog) ─────

  function withSuggestions(): StatementImportResponse {
    return {
      ...RESPONSE,
      lineItems: [
        // Tier-① learned → pre-applied, not a ghost.
        { ...RESPONSE.lineItems![0], suggestedPayeeId: 'p-learned', suggestedPayeeName: 'Costco', suggestionSource: 'Learned' },
        // Tier-② AI existing-payee ghost.
        { ...RESPONSE.lineItems![1], suggestedPayeeId: 'p-ai', suggestedPayeeName: 'Acme Payroll', suggestionSource: 'AiExistingPayee' },
        RESPONSE.lineItems![2],
      ],
    };
  }

  it('pre-applies a learned suggestion (not a ghost, learn=false) and leaves AI ghosts unapplied', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    const rows = c.lineItems();
    // Learned line 0: applied, not a ghost, not (yet) learned.
    expect(rows[0].payee).toEqual({ id: 'p-learned', name: 'Costco' });
    expect(c.isGhost(rows[0])).toBe(false);
    expect(rows[0].learn).toBe(false);
    // AI line 1: a ghost — unapplied until accepted.
    expect(rows[1].payee).toBeNull();
    expect(c.isGhost(rows[1])).toBe(true);
    expect(c.suggestionCount()).toBe(1);
  });

  it('accepting an AI ghost applies it and flags learn=true', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.acceptSuggestion(1);
    const row = c.lineItems()[1];
    expect(row.payee).toEqual({ id: 'p-ai', name: 'Acme Payroll' });
    expect(row.learn).toBe(true);
    expect(c.suggestionCount()).toBe(0);
  });

  it('a manual pick flags learn=true; an untouched learned pre-fill commits with learn=false', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Accept the ghost so commit isn't gated, then commit.
    c.acceptSuggestion(1);
    c.commit();

    const lines = api.lastCommit.body.lineItems;
    // Line 0 (learned, untouched) → applied but not taught.
    expect(lines.find((l: any) => l.index === 0)).toEqual(
      expect.objectContaining({ payeeId: 'p-learned', learn: false }));
    // Line 1 (accepted ghost) → taught.
    expect(lines.find((l: any) => l.index === 1)).toEqual(
      expect.objectContaining({ payeeId: 'p-ai', learn: true }));
  });

  it('Import gates on un-reviewed ghosts; "apply all & import" applies without learning', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // A ghost on a committable line remains → commit() opens the warning dialog, does NOT commit.
    expect(c.pendingGhostCount()).toBe(1);
    c.commit();
    expect(c.warnState()).toBe('open');
    expect(api.lastCommit).toBeNull();

    // Apply all & import → applies the ghost with learn=false, then commits.
    c.applyAllAndImport();
    await fixture.whenStable();
    expect(c.warnState()).toBe('closed');
    expect(api.lastCommit.body.lineItems.find((l: any) => l.index === 1)).toEqual(
      expect.objectContaining({ payeeId: 'p-ai', learn: false }));
  });

  it('bulk-assign fills only lines without a confirmed payee', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Line 0 already has the learned payee; line 1 is an unapplied ghost (payee null).
    c.bulkAssignPayee({
      id: 'p-bulk', name: 'Bulk', isArchived: false, transactionCount: 0, createdAt: '', updatedAt: '',
    });

    const rows = c.lineItems();
    expect(rows[0].payee).toEqual({ id: 'p-learned', name: 'Costco' }); // untouched — already has a payee
    expect(rows[1].payee).toEqual({ id: 'p-bulk', name: 'Bulk' });       // gap filled
    expect(rows[1].learn).toBe(true);
  });
});
