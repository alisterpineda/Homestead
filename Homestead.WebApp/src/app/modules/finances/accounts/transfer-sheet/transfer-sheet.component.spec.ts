import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TransferSheetComponent } from './transfer-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

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

const outgoingTransfer: TransactionResponse = {
  id: 'tx-1',
  shape: 'Transfer',
  occurredAt: '2026-04-23T00:00:00Z',
  memo: 'rent',
  splits: [],
  documentLinkCount: 0,
  details: {
    kind: 'transfer',
    sourceAccountId: 'cash-1',
    destinationAccountId: 'savings-1',
    sourceAmount: 500,
    destinationAmount: 500,
    sourceCurrencyId: 'cad',
    destinationCurrencyId: 'cad',
  } as never,
};

describe('TransferSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create mode', () => {
    const fixture = TestBed.createComponent(TransferSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    expect(c.direction()).toBe('outgoing');
    expect(c.counterpartyId()).toBeNull();
    expect(c.sourceAmount()).toBe('');
    expect(c.memo()).toBe('');
  });

  it('seeds from existing transaction in edit mode', () => {
    const fixture = TestBed.createComponent(TransferSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', outgoingTransfer);
    const c = fixture.componentInstance;

    expect(c.direction()).toBe('outgoing');
    expect(c.counterpartyId()).toBe('savings-1');
    expect(c.sourceAmount()).toBe('500');
    expect(c.memo()).toBe('rent');
    expect(c.date()).toBe('2026-04-23');
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = TestBed.createComponent(TransferSheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.counterpartyId.set('savings-1');
    c.sourceAmount.set('99.99');
    c.memo.set('user draft');
    c.date.set('2026-05-01');

    fixture.componentRef.setInput('account', { ...cashAccount });

    expect(c.counterpartyId()).toBe('savings-1');
    expect(c.sourceAmount()).toBe('99.99');
    expect(c.memo()).toBe('user draft');
    expect(c.date()).toBe('2026-05-01');
  });
});
