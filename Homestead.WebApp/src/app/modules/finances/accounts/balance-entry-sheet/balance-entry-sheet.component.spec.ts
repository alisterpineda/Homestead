import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { BalanceEntrySheetComponent } from './balance-entry-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

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

const investmentAccount: AccountResponse = {
  ...cashAccount,
  id: 'inv-1',
  name: 'TFSA',
  kind: 'Investment',
  nativeBalance: null,
};

const veqt: CommodityResponse = {
  id: 'veqt',
  symbol: 'VEQT',
  exchange: 'TSX',
  name: 'Vanguard All-Equity ETF',
  kind: 'Security',
  displayPrecision: 4,
  isSystemManaged: false,
  canDelete: false,
  latestPriceAt: null,
  priceCount: 0,
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

describe('BalanceEntrySheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(BalanceEntrySheetComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('blocks submission until the form is valid (Cash account)', () => {
    const fixture = TestBed.createComponent(BalanceEntrySheetComponent);
    fixture.componentRef.setInput('account', cashAccount);
    const c = fixture.componentInstance;

    expect(c.canSubmit()).toBe(false);

    c.commodityId.set('cad');
    c.amount.set('1500');
    c.date.set('2026-04-23');
    expect(c.canSubmit()).toBe(true);

    c.amount.set('-1');
    expect(c.canSubmit()).toBe(false);
  });

  it('settles the amount at the currency minor unit but leaves a security quantity free-precision', () => {
    const fixture = TestBed.createComponent(BalanceEntrySheetComponent);
    const c = fixture.componentInstance;

    // A security balance is a share count — rounding it to the commodity's *display* precision would
    // discard real holdings, so nothing is forced.
    c.onCommodityChange(veqt);
    c.commodity.set(veqt);
    expect(c.amountPrecision()).toBeNull();

    c.onCommodityChange(cad);
    c.commodity.set(cad);
    expect(c.amountPrecision()).toBe(2);
  });

  it('updates the active commodity id when a commodity is picked (Investment flow)', () => {
    const fixture = TestBed.createComponent(BalanceEntrySheetComponent);
    fixture.componentRef.setInput('account', investmentAccount);
    const c = fixture.componentInstance;

    c.onCommodityChange(veqt);
    expect(c.commodityId()).toBe('veqt');
  });

  it('preserves user input when account input re-emits with same id', () => {
    const fixture = TestBed.createComponent(BalanceEntrySheetComponent);
    fixture.componentRef.setInput('state', 'open');
    fixture.componentRef.setInput('account', cashAccount);
    fixture.componentRef.setInput('mode', 'create');
    const c = fixture.componentInstance;

    c.amount.set('1234.56');
    c.memo.set('user-typed memo');
    c.date.set('2026-04-23');

    fixture.componentRef.setInput('account', { ...cashAccount });

    expect(c.amount()).toBe('1234.56');
    expect(c.memo()).toBe('user-typed memo');
    expect(c.date()).toBe('2026-04-23');
  });
});
