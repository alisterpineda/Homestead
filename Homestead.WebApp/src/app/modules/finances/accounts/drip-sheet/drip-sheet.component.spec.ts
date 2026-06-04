import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { DripSheetComponent } from './drip-sheet.component';
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

// The Buy half is self-sufficient (K6): its TradeDetails carries account/security/currency/units/price,
// and amount = units × unitPrice = 0.05 × 100 = 5.
const buyHalf: TransactionResponse = {
  id: 'tx-buy',
  shape: 'InvestmentBuy',
  occurredAt: '2026-04-23T12:00:00.000Z',
  memo: 'Reinvest VFV dividend',
  relatedTransactionId: 'tx-div',
  splits: [],
  details: {
    kind: 'trade',
    accountId: 'inv-1',
    securityId: 'vfv',
    fundingCurrencyId: 'usd',
    quantity: 0.05,
    unitPrice: 100,
  } as never,
};

const dividendHalf: TransactionResponse = {
  id: 'tx-div',
  shape: 'Dividend',
  occurredAt: '2026-04-23T12:00:00.000Z',
  memo: 'Dividend',
  relatedTransactionId: 'tx-buy',
  splits: [],
  details: {
    kind: 'dividend',
    accountId: 'inv-1',
    paymentCurrencyId: 'usd',
    amount: 5,
  } as never,
};

function makeFixture() {
  const fixture = TestBed.createComponent(DripSheetComponent);
  fixture.componentRef.setInput('account', investmentAccount);
  fixture.componentRef.setInput('state', 'open');
  return fixture;
}

describe('DripSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('seeds defaults in create mode (reporting currency, empty amount/units)', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    expect(c.paymentCcyId()).toBe('cad');
    expect(c.securityId()).toBeNull();
    expect(c.amount()).toBe('');
    expect(c.units()).toBe('');
    expect(c.time()).toBeNull();
    expect(c.canSubmit()).toBe(false);
  });

  it('gates canSubmit on security + positive amount + positive units', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    expect(c.canSubmit()).toBe(false);
    c.amount.set('5');
    expect(c.canSubmit()).toBe(false); // still missing security + units
    c.units.set('0.05');
    expect(c.canSubmit()).toBe(false); // still missing security
    c.securityId.set('vfv');
    expect(c.canSubmit()).toBe(true);
    c.units.set('0');
    expect(c.canSubmit()).toBe(false);
  });

  it('derives the per-unit reinvestment price from amount ÷ units', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.amount.set('5');
    c.units.set('0.05');
    expect(c.pricePerUnit()).toBe(100);
    c.units.set('0');
    expect(c.pricePerUnit()).toBe(0);
  });

  it('seeds from the Buy half with no fetch when the Buy row is clicked', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', buyHalf);
    const c = fixture.componentInstance;

    expect(c.securityId()).toBe('vfv');
    expect(c.paymentCcyId()).toBe('usd');
    expect(c.units()).toBe('0.05');
    expect(c.amount()).toBe('5');
    expect(c.date()).toBe('2026-04-23');
    expect(c.time()).toBeNull();
    expect(c.description()).toBe('Reinvest VFV dividend');
    expect(c.pricePerUnit()).toBe(100);
    expect(c.canSubmit()).toBe(true);
  });

  it('survivorKind is the other half', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    const c = fixture.componentInstance;

    fixture.componentRef.setInput('existing', dividendHalf);
    expect(c.survivorKind()).toBe('buy');

    fixture.componentRef.setInput('existing', buyHalf);
    expect(c.survivorKind()).toBe('dividend');
  });

  it('stays unseeded for a clicked Dividend row until its partner Buy half resolves', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', dividendHalf);
    const c = fixture.componentInstance;

    // The Dividend half is NOT self-sufficient — fields stay blank until the partner fetch lands,
    // rather than mis-seeding from the dividend's own details.
    expect(c.securityId()).toBeNull();
    expect(c.units()).toBe('');
    expect(c.amount()).toBe('');
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.amount.set('42');
    c.units.set('0.5');
    c.description.set('draft note');

    fixture.componentRef.setInput('account', { ...investmentAccount });

    expect(c.amount()).toBe('42');
    expect(c.units()).toBe('0.5');
    expect(c.description()).toBe('draft note');
  });
});

describe('DripSheetComponent — partner fetch (Dividend row clicked)', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  it('fetches the Buy half via relatedTransactionId and seeds from it', async () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', dividendHalf);
    const c = fixture.componentInstance;

    // Note: whenStable() would deadlock here — the loading resource never settles until the
    // request is flushed, but the flush can't happen until whenStable() returns. Pump the
    // scheduler with detectChanges + a macrotask flush instead.
    fixture.detectChanges();
    await new Promise(r => setTimeout(r));

    // Exactly one partner round-trip, to the Buy half named by relatedTransactionId.
    const req = httpTesting.expectOne('/api/v1/finances/transactions/tx-buy');
    expect(req.request.method).toBe('GET');
    req.flush(buyHalf);

    await new Promise(r => setTimeout(r));
    fixture.detectChanges();

    expect(c.securityId()).toBe('vfv');
    expect(c.paymentCcyId()).toBe('usd');
    expect(c.units()).toBe('0.05');
    expect(c.amount()).toBe('5');
    expect(c.description()).toBe('Reinvest VFV dividend');
    // The re-seed kicks off commodity look-ups; they're left outstanding (no verify() in this
    // suite) since the partner fetch is the assertion here.
  });

  it('exposes a loading state while the partner fetch is in flight', async () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', dividendHalf);
    const c = fixture.componentInstance;

    fixture.detectChanges();
    await new Promise(r => setTimeout(r));

    httpTesting.expectOne('/api/v1/finances/transactions/tx-buy'); // fired, not yet flushed
    expect(c.partnerLoading()).toBe(true);
    expect(c.partnerLoadError()).toBe(false);
  });

  it('exposes an error state (not a blank form) when the partner fetch fails', async () => {
    const fixture = makeFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('existing', dividendHalf);
    const c = fixture.componentInstance;

    fixture.detectChanges();
    await new Promise(r => setTimeout(r));

    httpTesting.expectOne('/api/v1/finances/transactions/tx-buy')
      .flush({ detail: 'Not found' }, { status: 404, statusText: 'Not Found' });

    await new Promise(r => setTimeout(r));
    fixture.detectChanges();

    expect(c.partnerLoading()).toBe(false);
    expect(c.partnerLoadError()).toBe(true);
    // The form never seeds, so it can't be silently submitted with blank data.
    expect(c.securityId()).toBeNull();
    expect(c.canSubmit()).toBe(false);
  });
});
