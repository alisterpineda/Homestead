import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, NEVER, BehaviorSubject } from 'rxjs';
import { StatementImportReviewPageComponent } from './statement-import-review-page.component';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { FinancesAccountApiService } from '@/api/services/finances-account-api.service';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { FinancesSettingsApiService } from '@/api/services/finances-settings-api.service';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import type { StatementImportResponse } from '@/api/generated/models/statement-import-response';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

// The line-item table's Category combobox cells measure their trigger width via ResizeObserver, absent in jsdom.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

// Header opening 1000 → closing 1500. The two mappable lines (Expense 100, Income 600) net +500 on an asset
// account, so the default manifest reconciles exactly (checksum 'balanced'). The third line is un-mappable
// (missing amount + date) → included by default but needs-fixing, so it blocks Confirm until fixed/excluded.
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
    { index: 0, date: '2026-06-03', description: 'GROCERY', amount: 100, direction: 'Expense', runningBalance: null, mappable: true, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryId: null, suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
    { index: 1, date: '2026-06-15', description: 'PAYROLL', amount: 600, direction: 'Income', runningBalance: null, mappable: true, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryId: null, suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
    { index: 2, date: null, description: 'MYSTERY', amount: 0, direction: 'Expense', runningBalance: null, mappable: false, duplicateSuspected: false, accepted: true, suggestedPayeeId: null, suggestedPayeeName: null, suggestionSource: 'None', suggestedPayeeDefaultCategoryId: null, suggestedPayeeDefaultCategoryName: null, suggestedPayeeDefaultCategoryType: null },
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

const cat = (
  id: string, name: string, type: 'Expense' | 'Income',
): CategoryResponse => ({
  id, name, type, parentId: null, depth: 0, path: name, isSystem: false, readOnly: false,
});
// Both directions, as the page loads them once and passes into each row's combobox.
const CATEGORIES: CategoryResponse[] = [
  cat('e-groceries', 'Groceries', 'Expense'),
  cat('e-rent', 'Rent', 'Expense'),
  cat('i-salary', 'Salary', 'Income'),
];
const GROCERIES = { id: 'e-groceries', name: 'Groceries', type: 'Expense' as const };
const RENT = { id: 'e-rent', name: 'Rent', type: 'Expense' as const };
const SALARY = { id: 'i-salary', name: 'Salary', type: 'Income' as const };

// A PayeeResponse carrying an optional default category (drives the payee → category pre-fill).
function payee(id: string, name: string, def?: { id: string; name: string; type: 'Expense' | 'Income' }): PayeeResponse {
  return {
    id, name, isArchived: false, transactionCount: 0, createdAt: '', updatedAt: '',
    defaultCategoryId: def?.id ?? null,
    defaultCategoryName: def?.name ?? null,
    defaultCategoryType: def?.type ?? null,
  };
}

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
  // Controllable so a test can simulate a late finance-settings resolution that flips autoCat (re-seed).
  let settings$: BehaviorSubject<{ autoCategoriseFromPayee: boolean | null }>;

  function configure(response: StatementImportResponse = RESPONSE, account: AccountResponse = ASSET_ACCOUNT): void {
    api = new StubStatementApi();
    api.response = response;
    accountApi = new StubAccountApi();
    accountApi.account = account;
    payeeApi = new StubPayeeApi();
    settings$ = new BehaviorSubject<{ autoCategoriseFromPayee: boolean | null }>({ autoCategoriseFromPayee: true });
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinancesStatementApiService, useValue: api },
        { provide: FinancesAccountApiService, useValue: accountApi },
        { provide: FinancesPayeeApiService, useValue: payeeApi },
        { provide: FinancesCategoryApiService, useValue: { getCategories: () => of(CATEGORIES) } },
        { provide: FinancesSettingsApiService, useValue: { getFinanceSettings: () => settings$.asObservable() } },
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
    // Seeded through the account currency's precision — the server's 1500 reads as money on arrival.
    expect(c.amount()).toBe('1500.00');
    expect(c.date()).toBe('2026-06-30');
    // The un-mappable row 2 is included-but-needs-fixing, so Confirm is blocked until it's fixed/excluded.
    expect(c.canCommit()).toBe(false);
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

  it('renders a row per line item in the table', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Transactions (3)');
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

  // ─── Manifest model — included by default, needs-fix blocks Confirm ─────────

  it('seeds the manifest: all rows included by default; the un-mappable row needs fixing and blocks Confirm', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    const rows = c.allRows();
    expect(rows.length).toBe(3);
    expect(rows.every((r) => !r.excluded)).toBe(true);            // nothing excluded to start
    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]); // the two mappable rows import
    expect(c.needsFix(rows[2])).toBe(true);                        // the un-mappable one
    expect(c.needsFixCount()).toBe(1);
    expect(c.canCommit()).toBe(false);                            // …and blocks Confirm
  });

  it('exclude drops a row from the commit set and checksum; the Excluded filter shows it; Include restores it', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]);
    expect(c.checksum().state).toBe('balanced');

    c.toggleExcluded(0); // drop the −100 expense
    expect(c.allRows()[0].excluded).toBe(true);
    expect(c.linesToImport().map((r) => r.index)).toEqual([1]);
    // opening 1000 + 600 = 1600 vs closing 1500 → off.
    expect(c.checksum().state).toBe('off');

    // The Excluded filter surfaces it.
    c.filterMode.set('excluded');
    expect(c.visibleLineItems().map((r) => r.index)).toEqual([0]);

    // Include restores it.
    c.toggleExcluded(0);
    expect(c.allRows()[0].excluded).toBe(false);
    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]);
    expect(c.checksum().state).toBe('balanced');
  });

  it('a needs-fix row blocks Confirm until it is excluded OR fixed', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.canCommit()).toBe(false);

    // Excluding the needs-fix row unblocks Confirm.
    c.toggleExcluded(2);
    expect(c.canCommit()).toBe(true);

    // Re-include it → blocked again; fixing it (amount + date) also unblocks.
    c.toggleExcluded(2);
    expect(c.canCommit()).toBe(false);
    c.patchRow(2, { amount: '50', date: '2026-06-20' });
    expect(c.needsFix(c.allRows()[2])).toBe(false);
    expect(c.canCommit()).toBe(true);
  });

  // ─── Reset to original ──────────────────────────────────────────────────────

  it('reset restores an edited row and clears the dirty flag', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.isDirty(0)).toBe(false);
    c.patchRow(0, { amount: '999' });
    expect(c.isDirty(0)).toBe(true);

    c.resetRow(0);
    // Restores the FORMATTED original. If the snapshot held a bare '100' while the cell displayed
    // '100.00', reset would leave the row permanently dirty against its own original.
    expect(c.allRows()[0].amount).toBe('100.00');
    expect(c.isDirty(0)).toBe(false);
  });

  it('reset returns an overridden learned pre-fill to the learned payee', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Row 0 seeds the learned Costco pre-fill. Override it to a different payee.
    c.onRowPayeeChange(0, {
      id: 'p-other', name: 'Other', isArchived: false, transactionCount: 0, createdAt: '', updatedAt: '',
    });
    expect(c.allRows()[0].payee).toEqual({ id: 'p-other', name: 'Other' });
    expect(c.isDirty(0)).toBe(true);

    c.resetRow(0);
    expect(c.allRows()[0].payee).toEqual({ id: 'p-learned', name: 'Costco' });
    expect(c.allRows()[0].learn).toBe(false);
    expect(c.isDirty(0)).toBe(false);
  });

  // ─── Manual rows ─────────────────────────────────────────────────────────────

  it('addManualRow appends a needs-fix row that blocks Confirm; filling it makes it committable', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2); // clear the pre-existing needs-fix so the manual row's effect is isolated
    expect(c.canCommit()).toBe(true);

    c.addManualRow();
    const manual = c.allRows().find((r) => r.manual)!;
    expect(manual).toBeDefined();
    expect(manual.index).toBeLessThan(0);       // negative client key
    expect(c.needsFix(manual)).toBe(true);      // empty amount
    expect(c.canCommit()).toBe(false);          // blocks Confirm

    c.patchRow(manual.index, { amount: '42', date: '2026-06-20' });
    expect(c.canCommit()).toBe(true);
  });

  it('commit sends a manual row with index null and learn false; deleteManualRow removes it', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2); // clear the un-mappable needs-fix row

    c.addManualRow();
    const key = c.allRows().find((r) => r.manual)!.index;
    c.patchRow(key, { amount: '42', date: '2026-06-20', description: 'Cash tip' });
    c.commit();

    const manualLine = api.lastCommit.body.lineItems.find((l: any) => l.amount === 42);
    expect(manualLine).toEqual(
      expect.objectContaining({ index: null, learn: false, description: 'Cash tip', payeeId: null }));

    // Delete removes it from the manifest.
    c.deleteManualRow(key);
    expect(c.allRows().some((r) => r.manual)).toBe(false);
  });

  it('a manual row survives an autoCat settings re-seed (does not vanish)', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.addManualRow();
    const key = c.allRows().find((r) => r.manual)!.index;
    expect(c.allRows().some((r) => r.index === key)).toBe(true);

    // Late settings resolution flips autoCat → re-seeds parsedRows (jobId unchanged).
    settings$.next({ autoCategoriseFromPayee: false });
    fixture.detectChanges();
    await fixture.whenStable();

    // The manual row is still there.
    expect(c.allRows().some((r) => r.index === key && r.manual)).toBe(true);
  });

  // ─── Selection is transient (bulk-target only) ──────────────────────────────

  it('selection starts empty and no longer affects what commits', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.selectionCount()).toBe(0);

    c.toggleAll(true); // every row is selectable now (including the un-mappable one)
    expect(c.selectionCount()).toBe(3);

    // Picking Uncategorised · Income is how a bulk direction change is expressed now.
    c.bulkSetCategory({ direction: 'Income', categoryId: null, category: null });
    const rows = c.allRows();
    expect(rows[0].direction).toBe('Income');
    expect(rows[1].direction).toBe('Income');
    expect(rows[2].direction).toBe('Income');

    // Selection didn't change the commit set — row 2 is still needs-fix, out of linesToImport.
    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]);

    c.toggleAll(false);
    expect(c.selectionCount()).toBe(0);
  });

  it('bulk-sets category (and its direction) across the current selection, skipping unselected rows', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.setSelected(0, true);
    c.setSelected(1, true);
    c.bulkSetCategory({ direction: 'Income', categoryId: 'i-salary', category: SALARY });
    const rows = c.allRows();
    expect(rows[0].direction).toBe('Income');
    expect(rows[0].category).toEqual(SALARY);
    expect(rows[0].categoryTouched).toBe(true);
    expect(rows[1].direction).toBe('Income');
    expect(rows[2].direction).toBe('Expense'); // unselected → untouched
    expect(rows[2].category).toBeNull();
  });

  it('bulk exclude / include acts on selected parsed rows', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.setSelected(0, true);
    c.setSelected(1, true);
    c.bulkSetExcluded(true);
    expect(c.allRows()[0].excluded).toBe(true);
    expect(c.allRows()[1].excluded).toBe(true);
    expect(c.allRows()[2].excluded).toBe(false); // unselected

    c.bulkSetExcluded(false);
    expect(c.allRows()[0].excluded).toBe(false);
    expect(c.allRows()[1].excluded).toBe(false);
  });

  it('bulk-assigns then clears the payee across the selection', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.setSelected(0, true);
    c.setSelected(1, true);
    c.bulkAssignPayee({
      id: 'payee-hydro', name: 'Hydro', isArchived: false, transactionCount: 0, createdAt: '', updatedAt: '',
    });
    expect(c.allRows()[0].payee).toEqual({ id: 'payee-hydro', name: 'Hydro' });
    expect(c.allRows()[1].payee).toEqual({ id: 'payee-hydro', name: 'Hydro' });
    expect(c.allRows()[2].payee).toBeNull(); // unselected

    c.bulkClearPayee();
    expect(c.allRows()[0].payee).toBeNull();
    expect(c.allRows()[1].payee).toBeNull();
  });

  // ─── Advisory checksum ──────────────────────────────────────────────────────

  it('checksum is balanced with the default manifest on an asset account', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.checksum().state).toBe('balanced');
  });

  it('checksum goes off-by when a reconciling line is excluded', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(1); // drop the +600 income → expected 900 vs closing 1500
    const ck = c.checksum();
    expect(ck.state).toBe('off');
    expect(c.offAmount()).toContain('600');
  });

  it('checksum skips a non-numeric amount instead of reporting "Off by NaN"', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Reachable now that the cell is free text: a mid-edit or fat-fingered amount used to poison the
    // whole sum. Such a row can't commit anyway, so the checksum reports on the rest.
    c.patchRow(1, { amount: '6oo' });

    const ck = c.checksum();
    expect(ck.state).toBe('off');
    expect(c.offAmount()).not.toContain('NaN');
    // Same arithmetic as dropping the line entirely — the +600 income is simply absent.
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

  // ─── Commit payload ─────────────────────────────────────────────────────────

  it('commit() sends the balance entry and the included, edited line items', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2); // fix-or-exclude the needs-fix row so Confirm is enabled
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
        { index: 0, direction: 'Expense', amount: 100, date: '2026-06-03', description: 'GROCERY', payeeId: null, categoryId: null, learn: false },
        { index: 1, direction: 'Income', amount: 600, date: '2026-06-15', description: 'PAYROLL', payeeId: null, categoryId: null, learn: false },
      ],
    });
  });

  it('commit() carries a per-line payee pick through to the request', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2);
    // Pick a payee on the first included line (index 0).
    c.onRowPayeeChange(0, {
      id: 'payee-costco', name: 'Costco', isArchived: false, transactionCount: 0,
      createdAt: '', updatedAt: '',
    });
    c.commit();

    expect(api.lastCommit.body.lineItems[0]).toEqual(
      expect.objectContaining({ description: 'GROCERY', payeeId: 'payee-costco' }));
    expect(api.lastCommit.body.lineItems[1].payeeId).toBeNull();
  });

  it('filters visible rows while hidden-but-included rows still commit', async () => {
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
    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]);

    // Filter to duplicates → only row 1 visible, but row 0 stays included and commit-bound.
    c.filterMode.set('duplicates');
    expect(c.visibleLineItems().map((r) => r.index)).toEqual([1]);
    expect(c.linesToImport().map((r) => r.index)).toEqual([0, 1]);

    // 'needsfix' → only the un-mappable row 2.
    c.filterMode.set('needsfix');
    expect(c.visibleLineItems().map((r) => r.index)).toEqual([2]);
  });

  // ─── Payee suggestions (learned / ghost / learn flag / warning dialog) ─────

  function withSuggestions(): StatementImportResponse {
    return {
      ...RESPONSE,
      lineItems: [
        // Tier-① learned → pre-applied, not a ghost. Carries a default Expense category (Groceries) that
        // pre-fills the line's category on the matching-direction (Expense) line.
        {
          ...RESPONSE.lineItems![0], suggestedPayeeId: 'p-learned', suggestedPayeeName: 'Costco', suggestionSource: 'Learned',
          suggestedPayeeDefaultCategoryId: 'e-groceries', suggestedPayeeDefaultCategoryName: 'Groceries', suggestedPayeeDefaultCategoryType: 'Expense',
        },
        // Tier-② AI existing-payee ghost with a default Income category (Salary), matching the line direction
        // so accept pre-fills it.
        {
          ...RESPONSE.lineItems![1], suggestedPayeeId: 'p-ai', suggestedPayeeName: 'Acme Payroll', suggestionSource: 'AiExistingPayee',
          suggestedPayeeDefaultCategoryId: 'i-salary', suggestedPayeeDefaultCategoryName: 'Salary', suggestedPayeeDefaultCategoryType: 'Income',
        },
        RESPONSE.lineItems![2],
      ],
    };
  }

  it('pre-applies a learned suggestion (not a ghost, learn=false) and leaves AI ghosts unapplied', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    const rows = c.allRows();
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
    const row = c.allRows()[1];
    expect(row.payee).toEqual({ id: 'p-ai', name: 'Acme Payroll' });
    expect(row.learn).toBe(true);
    expect(c.suggestionCount()).toBe(0);
  });

  it('dismissing an AI ghost drops it from the count and imports the line unattributed', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2); // clear the un-mappable needs-fix row so commit can proceed

    // Line 1 is an un-accepted AI ghost that would otherwise gate the import.
    expect(c.suggestionCount()).toBe(1);
    expect(c.pendingGhostCount()).toBe(1);

    c.dismissSuggestion(1);

    // No longer a ghost → not counted, no import-gate.
    expect(c.allRows()[1].dismissed).toBe(true);
    expect(c.isGhost(c.allRows()[1])).toBe(false);
    expect(c.suggestionCount()).toBe(0);
    expect(c.pendingGhostCount()).toBe(0);

    // Commit goes straight through (no warning dialog) and the line imports unattributed.
    c.commit();
    expect(c.warnState()).toBe('closed');
    expect(api.lastCommit.body.lineItems.find((l: any) => l.index === 1)).toEqual(
      expect.objectContaining({ payeeId: null, learn: false }));
  });

  it('a manual pick flags learn=true; an untouched learned pre-fill commits with learn=false', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2);
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

    c.toggleExcluded(2); // clear the needs-fix gate so the ghost path is what's exercised

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

    c.setSelected(0, true);
    c.setSelected(1, true);
    // Line 0 already has the learned payee; line 1 is an unapplied ghost (payee null).
    c.bulkAssignPayee({
      id: 'p-bulk', name: 'Bulk', isArchived: false, transactionCount: 0, createdAt: '', updatedAt: '',
    });

    const rows = c.allRows();
    expect(rows[0].payee).toEqual({ id: 'p-learned', name: 'Costco' }); // untouched — already has a payee
    expect(rows[1].payee).toEqual({ id: 'p-bulk', name: 'Bulk' });       // gap filled
    expect(rows[1].learn).toBe(true);
  });

  // ─── Category ↔ payee coupling + client-authoritative commit ────────────────

  it('R1: category tracks the payee while pristine, then sticks once the reviewer edits it', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Pristine: applying a payee with an Expense default fills the Expense line's category.
    c.onRowPayeeChange(0, payee('p1', 'Grocer', GROCERIES));
    expect(c.allRows()[0].category).toEqual(GROCERIES);
    expect(c.allRows()[0].categoryTouched).toBe(false);

    // Explicit pick → sticky.
    c.setCategory(0, { direction: 'Expense', categoryId: 'e-rent', category: RENT });
    expect(c.allRows()[0].category).toEqual(RENT);
    expect(c.allRows()[0].categoryTouched).toBe(true);

    // A later payee (different Expense default) no longer overrides the sticky choice.
    c.onRowPayeeChange(0, payee('p2', 'Other', GROCERIES));
    expect(c.allRows()[0].category).toEqual(RENT);
  });

  it('R2: a payee never changes direction and only fills a category matching the current direction', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Row 0 is Expense. A payee whose default is an INCOME category can't fill it (direction filter),
    // and the payee never flips the line's direction.
    c.onRowPayeeChange(0, payee('p-inc', 'Employer', SALARY));
    expect(c.allRows()[0].direction).toBe('Expense');
    expect(c.allRows()[0].category).toBeNull();
    expect(c.allRows()[0].payeeDefaultCategory).toEqual(SALARY); // raw default kept (live Suggested source)
  });

  it('R3: the live Suggested source stays the payee default under a sticky override', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.onRowPayeeChange(0, payee('p1', 'Grocer', GROCERIES));
    c.setCategory(0, { direction: 'Expense', categoryId: 'e-rent', category: RENT }); // sticky override
    expect(c.allRows()[0].category).toEqual(RENT);
    // Suggested still reflects the CURRENT payee's default, not the override.
    expect(c.allRows()[0].payeeDefaultCategory).toEqual(GROCERIES);
  });

  it('R4: auto-uncategorised is pristine, but an explicit Uncategorised pick is sticky', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Seed: no payee → auto-uncategorised, pristine.
    expect(c.allRows()[0].category).toBeNull();
    expect(c.allRows()[0].categoryTouched).toBe(false);

    // Explicit Uncategorised · Expense pick → an edit (sticky).
    c.setCategory(0, { direction: 'Expense', categoryId: null, category: null });
    expect(c.allRows()[0].categoryTouched).toBe(true);

    // A later payee with a default no longer re-fills the (sticky) uncategorised choice.
    c.onRowPayeeChange(0, payee('p1', 'Grocer', GROCERIES));
    expect(c.allRows()[0].category).toBeNull();
  });

  it('R5: accept keeps the pristine guard; reset un-sticks the category', async () => {
    configure(withSuggestions());
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    // Tier-① learned row 0 pre-fills Groceries (Expense), pristine.
    expect(c.allRows()[0].category).toEqual(GROCERIES);
    expect(c.allRows()[0].categoryTouched).toBe(false);

    // Accept the AI ghost on Income line 1 → its Income default (Salary) pre-fills (pristine → derives).
    c.acceptSuggestion(1);
    expect(c.allRows()[1].category).toEqual(SALARY);
    expect(c.allRows()[1].categoryTouched).toBe(false);

    // Override row 0 (sticky), then reset → un-sticks back to the learned original.
    c.setCategory(0, { direction: 'Expense', categoryId: 'e-rent', category: RENT });
    expect(c.allRows()[0].categoryTouched).toBe(true);
    c.resetRow(0);
    expect(c.allRows()[0].category).toEqual(GROCERIES);
    expect(c.allRows()[0].categoryTouched).toBe(false);
  });

  it('a category pick flips direction and recomputes the advisory checksum', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    expect(c.checksum().state).toBe('balanced');
    // Flip the −100 Expense line to Income (+100): 1000 + 700 = 1700 vs closing 1500 → off.
    c.setCategory(0, { direction: 'Income', categoryId: 'i-salary', category: SALARY });
    expect(c.allRows()[0].direction).toBe('Income');
    expect(c.checksum().state).toBe('off');
  });

  it('onCategoryCreated merges an inline-created category into the shared list (idempotent on id)', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    const before = c.categories().length;
    const created = cat('e-new', 'Fuel', 'Expense');
    c.onCategoryCreated(created);
    expect(c.categories().some((x) => x.id === 'e-new')).toBe(true);
    expect(c.categories().length).toBe(before + 1);

    c.onCategoryCreated(created); // same id → no duplicate
    expect(c.categories().length).toBe(before + 1);
  });

  it('commit sends categoryId per line (null by default; a sticky override survives a later payee change)', async () => {
    configure();
    const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
    await fixture.whenStable();
    const c = fixture.componentInstance;

    c.toggleExcluded(2); // clear the needs-fix row so Confirm is enabled

    // Sticky Rent on line 0; a later payee whose default is Groceries must NOT override the sticky choice.
    c.setCategory(0, { direction: 'Expense', categoryId: 'e-rent', category: RENT });
    c.onRowPayeeChange(0, payee('p1', 'Grocer', GROCERIES));
    c.commit();

    const lines = api.lastCommit.body.lineItems;
    expect(lines.find((l: any) => l.index === 0)).toEqual(
      expect.objectContaining({ categoryId: 'e-rent', payeeId: 'p1' }));
    expect(lines.find((l: any) => l.index === 1).categoryId).toBeNull(); // untouched → uncategorised
  });

  // ─── Column sizing ──────────────────────────────────────────────────────────
  // jsdom reports zero-width layout, so getSize() returns TanStack's configured defaults. Assert on the
  // CSS-variable map and the table API — never on real pixel geometry.

  describe('column sizing', () => {
    const COLUMN_IDS = ['select', 'date', 'payee', 'category', 'description', 'amount', 'actions'];
    const SIZES: Record<string, number> = {
      select: 36, date: 190, payee: 240, category: 230, description: 190, amount: 100, actions: 44,
    };

    async function render() {
      configure();
      const fixture = TestBed.createComponent(StatementImportReviewPageComponent);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      return fixture;
    }

    it('emits a header/col CSS-variable pair per column at the configured width', async () => {
      const fixture = await render();
      const vars = fixture.componentInstance['columnSizeVars']();

      for (const id of COLUMN_IDS) {
        expect(vars[`--header-${id}-size`]).toBe(`${SIZES[id]}px`);
        expect(vars[`--col-${id}-size`]).toBe(`${SIZES[id]}px`);
      }
      expect(Object.keys(vars).length).toBe(COLUMN_IDS.length * 2);
    });

    it('reports not-resizing on a freshly rendered table', async () => {
      const fixture = await render();
      expect(fixture.componentInstance['isResizing']()).toBe(false);
    });

    it('disables resizing on select and actions only', async () => {
      const fixture = await render();
      const table = fixture.componentInstance['table'];
      const canResize = Object.fromEntries(
        COLUMN_IDS.map((id) => [id, table.getColumn(id)?.getCanResize()]));

      expect(canResize).toEqual({
        select: false, date: true, payee: true, category: true, description: true, amount: true, actions: false,
      });
    });

    // Regression guard: the shared line-category combobox keeps min-w-[11rem] (176px) because the
    // bulk-actions bar needs it outside any table. Dropping these minSizes below the control's floor
    // would let a drag squeeze the cell past what the control can render.
    it('keeps each minSize clear of its control’s intrinsic floor', async () => {
      const fixture = await render();
      const table = fixture.componentInstance['table'];
      const minSize = (id: string) => table.getColumn(id)?.columnDef.minSize;

      expect(minSize('category')).toBeGreaterThanOrEqual(192); // 176px trigger + 16px cell padding
      expect(minSize('payee')).toBeGreaterThanOrEqual(208);    // 192px input-group + 16px cell padding
    });

    // The bug this table was made resizable to fix: at 150px the date picker clipped `Feb 29, 2024` to
    // `Feb 2` on every row. 183px is the measured width at which it renders whole (77px of that column
    // is picker furniture — cell padding, input padding, clear + calendar buttons). Both the resting
    // width and the drag floor must clear it, or a narrow drag re-creates the original bug.
    it('keeps the date column wide enough to render a full date, at rest and at its drag floor', async () => {
      const fixture = await render();
      const date = fixture.componentInstance['table'].getColumn('date');

      expect(date?.columnDef.size).toBeGreaterThanOrEqual(183);
      expect(date?.columnDef.minSize).toBeGreaterThanOrEqual(183);
    });

    it('renders the table with fixed layout so column widths are authoritative', async () => {
      const fixture = await render();
      const table = (fixture.nativeElement as HTMLElement).querySelector('table');

      expect(table).not.toBeNull();
      expect(table!.style.tableLayout).toBe('fixed');
    });
  });
});
