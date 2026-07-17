import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { DividendSheetComponent } from './dividend-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

const investmentAccount: AccountResponse = {
  id: 'inv-1',
  name: 'Brokerage',
  accountType: 'Asset',
  kind: 'Investment',
  reportingCurrencyId: 'cad',
  institution: null,
  isSystem: false,
  isArchived: false,
  createdAt: '',
  updatedAt: '',
  displayValue: 5000,
  nativeBalance: null,
  unpricedHoldingCount: 0,
  hasTransactions: true,
};

const dividend: TransactionResponse = {
  id: 'tx-div',
  shape: 'Dividend',
  occurredAt: '2026-04-23T12:00:00.000Z',
  memo: 'VEQT distribution',
  splits: [],
  documentLinkCount: 0,
  details: {
    kind: 'dividend',
    accountId: 'inv-1',
    paymentCurrencyId: 'usd',
    amount: 12.5,
  } as never,
};

function makeFixture() {
  const fixture = TestBed.createComponent(DividendSheetComponent);
  fixture.componentRef.setInput('account', investmentAccount);
  fixture.componentRef.setInput('state', 'open');
  return fixture;
}

describe('DividendSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create mode (reporting currency, empty amount)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    expect(c.paymentCcyId()).toBe('cad');
    expect(c.amount()).toBe('');
    expect(c.time()).toBeNull();
    expect(c.canSubmit()).toBe(false);
  });

  it('seeds from an existing dividend in edit mode (foreign currency)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', dividend);
    const c = fixture.componentInstance;

    expect(c.paymentCcyId()).toBe('usd');
    expect(c.amount()).toBe('12.5');
    expect(c.date()).toBe('2026-04-23');
    expect(c.time()).toBeNull();
    expect(c.description()).toBe('VEQT distribution');
  });

  it('gates canSubmit on a positive amount', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    expect(c.canSubmit()).toBe(false);
    c.amount.set('10');
    expect(c.canSubmit()).toBe(true);
    c.amount.set('0');
    expect(c.canSubmit()).toBe(false);
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.amount.set('42');
    c.description.set('draft note');

    fixture.componentRef.setInput('account', { ...investmentAccount });

    expect(c.amount()).toBe('42');
    expect(c.description()).toBe('draft note');
  });
});
