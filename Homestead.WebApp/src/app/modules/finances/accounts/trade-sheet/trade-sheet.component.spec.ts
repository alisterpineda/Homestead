import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { TradeSheetComponent } from './trade-sheet.component';
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

const buyTrade: TransactionResponse = {
  id: 'tx-buy',
  shape: 'InvestmentBuy',
  occurredAt: '2026-04-23T12:00:00.000Z',
  memo: 'monthly buy',
  splits: [],
  documentLinkCount: 0,
  details: {
    kind: 'trade',
    accountId: 'inv-1',
    securityId: 'veqt',
    fundingCurrencyId: 'cad',
    quantity: 2,
    unitPrice: 50,
  } as never,
};

const sellTrade: TransactionResponse = {
  ...buyTrade,
  id: 'tx-sell',
  shape: 'InvestmentSell',
  memo: 'trim',
};

function makeFixture() {
  const fixture = TestBed.createComponent(TradeSheetComponent);
  fixture.componentRef.setInput('account', investmentAccount);
  fixture.componentRef.setInput('state', 'open');
  return fixture;
}

describe('TradeSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create mode (Buy, reporting currency funding)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    expect(c.selectedSide()).toBe('Buy');
    expect(c.securityId()).toBeNull();
    expect(c.quantity()).toBe('');
    expect(c.unitPrice()).toBe('');
    expect(c.fundingCcyId()).toBe('cad');
    expect(c.time()).toBeNull();
    expect(c.canSubmit()).toBe(false);
  });

  it('seeds a Buy from an existing transaction in edit mode', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', buyTrade);
    const c = fixture.componentInstance;

    expect(c.selectedSide()).toBe('Buy');
    expect(c.securityId()).toBe('veqt');
    expect(c.quantity()).toBe('2');
    expect(c.unitPrice()).toBe('50');
    expect(c.fundingCcyId()).toBe('cad');
    expect(c.date()).toBe('2026-04-23');
    expect(c.time()).toBeNull(); // noon-UTC collapses to date-only
    expect(c.memo()).toBe('monthly buy');
  });

  it('reads Sell side from the InvestmentSell shape', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', sellTrade);
    expect(fixture.componentInstance.selectedSide()).toBe('Sell');
  });

  it('seeds the create-mode side from the host-provided side input', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    fixture.componentRef.setInput('side', 'Sell');
    expect(fixture.componentInstance.selectedSide()).toBe('Sell');
  });

  it('hides the funding picker by default in create mode but reveals it in edit mode', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    // showFundingCcy defaults false and funding defaults to the reporting currency.
    expect(fixture.componentInstance.showFunding()).toBe(false);

    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', buyTrade);
    // Edit always reveals so a non-held funding currency stays viewable/editable.
    expect(fixture.componentInstance.showFunding()).toBe(true);
  });

  it('reveals the funding picker when the selected funding currency is non-reporting', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    expect(c.showFunding()).toBe(false);
    c.fundingCcyId.set('usd'); // != reporting 'cad'
    expect(c.showFunding()).toBe(true);
  });

  it('derives the total from quantity × price', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    c.quantity.set('3');
    c.unitPrice.set('25');
    expect(c.total()).toBe(75);
  });

  it('gates canSubmit on positive quantity and price', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;
    c.securityId.set('veqt');
    expect(c.canSubmit()).toBe(false); // no qty/price yet
    c.quantity.set('2');
    c.unitPrice.set('50');
    expect(c.canSubmit()).toBe(true);
    c.quantity.set('0');
    expect(c.canSubmit()).toBe(false);
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.securityId.set('veqt');
    c.quantity.set('7');
    c.unitPrice.set('12.34');

    fixture.componentRef.setInput('account', { ...investmentAccount });

    expect(c.securityId()).toBe('veqt');
    expect(c.quantity()).toBe('7');
    expect(c.unitPrice()).toBe('12.34');
  });
});
