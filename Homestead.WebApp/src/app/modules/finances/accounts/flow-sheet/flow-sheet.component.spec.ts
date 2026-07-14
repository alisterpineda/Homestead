import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { FlowSheetComponent } from './flow-sheet.component';
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
  nativeBalance: null,
  unpricedHoldingCount: 0,
  hasTransactions: true,
};

const incomeFlow: TransactionResponse = {
  id: 'tx-flow',
  shape: 'Income',
  occurredAt: '2026-04-23T00:00:00.000Z',
  memo: 'Refund',
  splits: [],
  details: {
    kind: 'flow',
    accountId: 'cash-1',
    currencyId: 'cad',
    amount: 30,
  } as never,
};

function makeFixture() {
  const fixture = TestBed.createComponent(FlowSheetComponent);
  fixture.componentRef.setInput('account', cashAccount);
  fixture.componentRef.setInput('state', 'open');
  return fixture;
}

describe('FlowSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create mode (empty amount, direction from input, cannot submit)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    fixture.componentRef.setInput('direction', 'Income');
    const c = fixture.componentInstance;

    expect(c.selectedDirection()).toBe('Income');
    expect(c.amount()).toBe('');
    expect(c.canSubmit()).toBe(false);
  });

  it('seeds from an existing flow in edit mode (amount, date, description, direction)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', incomeFlow);
    const c = fixture.componentInstance;

    expect(c.selectedDirection()).toBe('Income');
    expect(c.amount()).toBe('30');
    expect(c.date()).toBe('2026-04-23');
    expect(c.description()).toBe('Refund');
  });

  it('gates canSubmit on a positive amount', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    expect(c.canSubmit()).toBe(false);
    c.amount.set('50');
    expect(c.canSubmit()).toBe(true);
    c.amount.set('0');
    expect(c.canSubmit()).toBe(false);
  });

  it('locks direction to the stored shape in edit mode, ignoring the create-mode direction input', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    // The create-mode direction input says Expense, but the stored flow is Income:
    // edit mode derives the (immutable) direction from the shape.
    fixture.componentRef.setInput('direction', 'Expense');
    fixture.componentRef.setInput('existing', incomeFlow);
    const c = fixture.componentInstance;

    expect(c.selectedDirection()).toBe('Income');
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.amount.set('42');
    c.description.set('draft note');

    fixture.componentRef.setInput('account', { ...cashAccount });

    expect(c.amount()).toBe('42');
    expect(c.description()).toBe('draft note');
  });
});
