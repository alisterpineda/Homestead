import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { OpeningBalanceSheetComponent } from './opening-balance-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { OpeningBalanceResponse } from '@/api/generated/models/opening-balance-response';

const cashAccount: AccountResponse = {
  id: 'cash-1',
  name: 'Chequing',
  accountType: 'Asset',
  kind: 'Cash',
  reportingCurrencyId: 'cad',
  institution: null,
  isSystem: false,
  isArchived: false,
  createdAt: '',
  updatedAt: '',
  displayValue: 1000,
  nativeBalance: 1000,
  unpricedHoldingCount: 0,
  hasTransactions: true,
};

const cadOpeningBalance: OpeningBalanceResponse = {
  accountId: 'cash-1',
  asOfDate: '2026-01-01',
  commodityId: 'cad',
  memo: 'opening',
  quantity: 250,
  transactionId: 'tx-ob-1',
};

const veqtOpeningBalance: OpeningBalanceResponse = {
  accountId: 'cash-1',
  asOfDate: '2026-02-01',
  commodityId: 'veqt',
  memo: 'shares',
  quantity: 10,
  transactionId: 'tx-ob-2',
  valuePerUnit: 30,
};

describe('OpeningBalanceSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create-missing mode', () => {
    const fixture = TestBed.createComponent(OpeningBalanceSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'create-missing');
    const c = fixture.componentInstance;

    expect(c.quantity()).toBe('');
    expect(c.unitPrice()).toBe('');
    expect(c.memo()).toBe('');
  });

  it('seeds from existing opening balance in edit mode', () => {
    const fixture = TestBed.createComponent(OpeningBalanceSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', cadOpeningBalance);
    const c = fixture.componentInstance;

    expect(c.quantity()).toBe('250');
    expect(c.asOfDate()).toBe('2026-01-01');
    expect(c.memo()).toBe('opening');
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = TestBed.createComponent(OpeningBalanceSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', cadOpeningBalance);
    const c = fixture.componentInstance;

    c.quantity.set('999');
    c.memo.set('user draft');
    c.asOfDate.set('2026-03-15');

    fixture.componentRef.setInput('account', { ...cashAccount });
    fixture.componentRef.setInput('existing', { ...cadOpeningBalance });

    expect(c.quantity()).toBe('999');
    expect(c.memo()).toBe('user draft');
    expect(c.asOfDate()).toBe('2026-03-15');
  });

  it('reseeds when existing switches to a different commodityId', () => {
    const fixture = TestBed.createComponent(OpeningBalanceSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', cadOpeningBalance);
    const c = fixture.componentInstance;

    c.quantity.set('999');

    fixture.componentRef.setInput('existing', veqtOpeningBalance);

    expect(c.quantity()).toBe('10');
    expect(c.unitPrice()).toBe('30');
    expect(c.memo()).toBe('shares');
  });
});
