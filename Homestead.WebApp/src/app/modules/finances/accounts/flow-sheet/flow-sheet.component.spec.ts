import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { of } from 'rxjs';
import { FlowSheetComponent } from './flow-sheet.component';
import { FinancesTransactionApiService } from '@/api/services/finances-transaction-api.service';
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
  documentLinkCount: 0,
  details: {
    kind: 'flow',
    accountId: 'cash-1',
    currencyId: 'cad',
    amount: 30,
  } as never,
};

const categorizedExpenseFlow: TransactionResponse = {
  id: 'tx-cat',
  shape: 'Expense',
  occurredAt: '2026-04-23T00:00:00.000Z',
  memo: 'Groceries',
  splits: [],
  documentLinkCount: 0,
  details: {
    kind: 'flow',
    accountId: 'cash-1',
    currencyId: 'cad',
    amount: 40,
    categoryId: 'cat-groceries',
    categoryName: 'Groceries',
    categoryPath: 'Food › Groceries',
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

  it('defaults categoryId to null in create mode', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    expect(fixture.componentInstance.categoryId()).toBeNull();
  });

  it('seeds categoryId from an existing flow in edit mode', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', categorizedExpenseFlow);
    expect(fixture.componentInstance.categoryId()).toBe('cat-groceries');
  });

  it('clears the selected category when the direction is switched (create mode)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    c.categoryId.set('cat-groceries');
    c.setDirection('Income');
    expect(c.selectedDirection()).toBe('Income');
    expect(c.categoryId()).toBeNull();
  });
});

describe('FlowSheetComponent categoryId payload', () => {
  let createFlow: ReturnType<typeof vi.fn>;
  let patchFlow: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createFlow = vi.fn().mockReturnValue(of(incomeFlow));
    patchFlow = vi.fn().mockReturnValue(of(incomeFlow));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        {
          provide: FinancesTransactionApiService,
          useValue: { createFlow, patchFlow, deleteTransaction: vi.fn().mockReturnValue(of(void 0)) },
        },
      ],
    });
  });

  it('sends categoryId in the create payload', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    c.amount.set('50');
    c.categoryId.set('cat-1');
    c.handleSave();

    expect(createFlow).toHaveBeenCalledTimes(1);
    expect(createFlow.mock.calls[0][0].body).toEqual(expect.objectContaining({ categoryId: 'cat-1' }));
  });

  it('sends categoryId in the edit (patch) payload', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', categorizedExpenseFlow);
    const c = fixture.componentInstance;
    c.categoryId.set(null); // recategorise to Uncategorised
    c.handleSave();

    expect(patchFlow).toHaveBeenCalledTimes(1);
    expect(patchFlow.mock.calls[0][0].body).toEqual(expect.objectContaining({ categoryId: null }));
  });
});
