import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { of } from 'rxjs';
import { AccountActivityTabComponent } from './account-activity-tab.component';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import {
  FinancesTransactionApiService,
  FinancesAccountApiService,
  FinancesCommodityApiService,
} from '@/api/services';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

const cashAccount: AccountResponse = {
  id: 'acc-1',
  name: 'Chequing',
  accountType: 'Asset',
  kind: 'Cash',
  reportingCurrencyId: 'cad',
  institution: null,
  isSystem: false,
  isArchived: false,
  createdAt: '',
  updatedAt: '',
  displayValue: 0,
  nativeBalance: null,
  unpricedHoldingCount: 0,
  hasTransactions: true,
};

const cad: CommodityResponse = {
  id: 'cad',
  symbol: 'CAD',
  exchange: null,
  name: 'Canadian Dollar',
  kind: 'Currency',
  displayPrecision: 2,
  isSystemManaged: true,
  canDelete: false,
  latestPriceAt: null,
  priceCount: 0,
};

class FakeSettings {
  displayCurrency = () => cad;
}

function makeTx(overrides: Partial<TransactionResponse> & { id: string }): TransactionResponse {
  return {
    shape: 'Expense',
    memo: 'Groceries',
    occurredAt: '2026-04-23T00:00:00Z',
    relatedTransactionId: null,
    documentLinkCount: 0,
    splits: [
      { id: `${overrides.id}-s1`, accountId: 'acc-1', commodityId: 'cad', quantity: -50 },
      { id: `${overrides.id}-s2`, accountId: 'sys-expense', commodityId: 'cad', quantity: 50 },
    ],
    ...overrides,
  };
}

describe('AccountActivityTabComponent', () => {
  let txApiStub: {
    getAccountTransactions: ReturnType<typeof vi.fn>;
    bulkDeleteTransactions: ReturnType<typeof vi.fn>;
    getTransactionDocuments: ReturnType<typeof vi.fn>;
  };

  function setup(items: TransactionResponse[], totalCount = items.length) {
    txApiStub = {
      getAccountTransactions: vi.fn(() => of({ page: 1, pageSize: 25, totalCount, items })),
      bulkDeleteTransactions: vi.fn(() => of({ deletedCount: 2 })),
      // The hosted linked-documents dialog fetches through this once a transaction is targeted.
      getTransactionDocuments: vi.fn(() => of([])),
    };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinanceSettingsService, useClass: FakeSettings },
        { provide: FinancesTransactionApiService, useValue: txApiStub },
        { provide: FinancesAccountApiService, useValue: { getAccount: vi.fn(() => of(cashAccount)) } },
        { provide: FinancesCommodityApiService, useValue: { getCommodity: vi.fn(() => of(cad)) } },
      ],
    });
    TestBed.inject(UserPreferencesService).financesPrivacyMasked.set(false);
    const fixture = TestBed.createComponent(AccountActivityTabComponent);
    fixture.componentRef.setInput('account', cashAccount);
    fixture.detectChanges();
    return fixture;
  }

  it('renders a selection checkbox for each row plus a select-all header checkbox', () => {
    const fixture = setup([makeTx({ id: 't1' }), makeTx({ id: 't2' })]);
    const checkboxes = fixture.nativeElement.querySelectorAll('hlm-checkbox');
    expect(checkboxes.length).toBe(3);
  });

  it('disables selection on OpeningBalance and Other rows', () => {
    const fixture = setup([
      makeTx({ id: 'ob', shape: 'OpeningBalance' }),
      makeTx({ id: 'other', shape: 'Other' }),
      makeTx({ id: 'flow' }),
    ]);
    const table = fixture.componentInstance['table'];
    expect(table.getRow('ob').getCanSelect()).toBe(false);
    expect(table.getRow('other').getCanSelect()).toBe(false);
    expect(table.getRow('flow').getCanSelect()).toBe(true);
  });

  it('select-all selects only selectable rows', () => {
    const fixture = setup([
      makeTx({ id: 'ob', shape: 'OpeningBalance' }),
      makeTx({ id: 't1' }),
      makeTx({ id: 't2' }),
    ]);
    const c = fixture.componentInstance;
    c['table'].toggleAllRowsSelected(true);
    expect(c.selectedIds().sort()).toEqual(['t1', 't2']);
  });

  it('shows the selection bar with count when rows are selected', () => {
    const fixture = setup([makeTx({ id: 't1' }), makeTx({ id: 't2' })]);
    const c = fixture.componentInstance;

    expect(fixture.nativeElement.textContent).not.toContain('selected');

    c['table'].getRow('t1').toggleSelected(true);
    fixture.detectChanges();

    expect(c.selectionCount()).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('1 transaction selected');
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    expect(buttons.some(b => b.textContent?.includes('Delete'))).toBe(true);
  });

  it('bulk delete calls the API with selected ids, clears selection, and emits transactionsDeleted', async () => {
    const fixture = setup([makeTx({ id: 't1' }), makeTx({ id: 't2' })]);
    const c = fixture.componentInstance;
    let deletedEmitted = false;
    c.transactionsDeleted.subscribe(() => { deletedEmitted = true; });

    c['table'].toggleAllRowsSelected(true);
    const close = vi.fn();
    c.onBulkDelete({ close });
    await fixture.whenStable();

    expect(txApiStub.bulkDeleteTransactions).toHaveBeenCalledWith({ body: { ids: ['t1', 't2'] } });
    expect(close).toHaveBeenCalled();
    expect(c.hasSelection()).toBe(false);
    expect(deletedEmitted).toBe(true);
  });

  it('clears selection when a refresh re-fetches the list', () => {
    const fixture = setup([makeTx({ id: 't1' })]);
    const c = fixture.componentInstance;

    c['table'].getRow('t1').toggleSelected(true);
    expect(c.hasSelection()).toBe(true);

    // Parent bumps refreshTrigger after any sheet save/delete; the refetched
    // page may hold different rows, so stale selections must be discarded.
    fixture.componentRef.setInput('refreshKey', 1);
    fixture.detectChanges();

    expect(c.hasSelection()).toBe(false);
  });

  it('clears selection on page change', () => {
    const fixture = setup([makeTx({ id: 't1' })], 60);
    const c = fixture.componentInstance;

    c['table'].getRow('t1').toggleSelected(true);
    expect(c.hasSelection()).toBe(true);

    c.nextPage();
    fixture.detectChanges();

    expect(c.hasSelection()).toBe(false);
  });

  it('flags reinvested-dividend selections for the dialog copy', () => {
    const fixture = setup([
      makeTx({ id: 'div', shape: 'Dividend', relatedTransactionId: 'buy' }),
      makeTx({ id: 't1' }),
    ]);
    const c = fixture.componentInstance;

    c['table'].getRow('t1').toggleSelected(true);
    expect(c.selectionIncludesDrip()).toBe(false);

    c['table'].getRow('div').toggleSelected(true);
    expect(c.selectionIncludesDrip()).toBe(true);
  });

  it('openLinkedDocuments targets the transaction and opens the dialog', () => {
    const tx = makeTx({ id: 't1', documentLinkCount: 2 });
    const fixture = setup([tx]);
    const c = fixture.componentInstance;

    expect(c.linkedDocsState()).toBe('closed');
    expect(c.linkedDocsTxId()).toBeNull();

    c.openLinkedDocuments(tx);

    expect(c.linkedDocsTxId()).toBe('t1');
    expect(c.linkedDocsState()).toBe('open');
  });

  it('onLinkedDocsClosed resets the dialog state and clears the transaction id', () => {
    const tx = makeTx({ id: 't1' });
    const fixture = setup([tx]);
    const c = fixture.componentInstance;

    c.openLinkedDocuments(tx);
    c.onLinkedDocsClosed();

    expect(c.linkedDocsState()).toBe('closed');
    // Cleared so re-opening the same transaction registers as an id change → exactly one fetch.
    expect(c.linkedDocsTxId()).toBeNull();
  });

  it('renders the payee as the primary memo line with the memo as muted secondary', () => {
    const fixture = setup([
      makeTx({
        id: 'flow-payee',
        memo: 'Weekly shop',
        details: {
          kind: 'flow', accountId: 'acc-1', currencyId: 'cad', amount: 50,
          categoryId: null, categoryName: null, categoryPath: null,
          payeeId: 'p1', payeeName: 'Costco',
        } as never,
      }),
    ]);
    const c = fixture.componentInstance;
    const vm = c.items().find(v => v.tx.id === 'flow-payee');
    expect(vm?.payeeName).toBe('Costco');

    const memoCell = fixture.nativeElement.querySelector('app-activity-memo-cell') as HTMLElement;
    expect(memoCell.textContent).toContain('Costco');
    expect(memoCell.textContent).toContain('Weekly shop');
    // The memo renders in the muted secondary class; the payee does not.
    expect(memoCell.querySelector('.text-muted-foreground')?.textContent).toContain('Weekly shop');
  });

  it('renders a single memo line when a flow has no payee', () => {
    const fixture = setup([makeTx({ id: 'flow-nopayee', memo: 'Groceries' })]);
    const vm = fixture.componentInstance.items().find(v => v.tx.id === 'flow-nopayee');
    expect(vm?.payeeName).toBeNull();
  });

  it('treats only the select and actions columns as row-click dead zones', () => {
    const fixture = setup([makeTx({ id: 't1' })]);
    const c = fixture.componentInstance;

    expect(c['isDeadZone']('select')).toBe(true);
    expect(c['isDeadZone']('actions')).toBe(true);
    expect(c['isDeadZone']('date')).toBe(false);
    expect(c['isDeadZone']('type')).toBe(false);
    expect(c['isDeadZone']('memo')).toBe(false);
    expect(c['isDeadZone']('amount')).toBe(false);
  });
});
