import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { AddPositionSheetComponent } from './add-position-sheet.component';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const investmentAccount: AccountResponse = {
  id: 'inv-1',
  name: 'TFSA',
  accountType: 'Asset',
  kind: 'Investment',
  reportingCurrencyId: 'cad',
  institution: null,
  isSystem: false,
  isArchived: false,
  createdAt: '',
  updatedAt: '',
  displayValue: 0,
  nativeBalance: null,
  unpricedHoldingCount: 0,
  hasTransactions: false,
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

const usd: CommodityResponse = {
  id: 'usd',
  symbol: 'USD',
  exchange: null,
  name: 'US Dollar',
  kind: 'Currency',
  displayPrecision: 2,
  isSystemManaged: true,
  canDelete: false,
  latestPriceAt: null,
  priceCount: 0,
};

describe('AddPositionSheetComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient()],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(AddPositionSheetComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the unit-price input only for security commodities', () => {
    const fixture = TestBed.createComponent(AddPositionSheetComponent);
    const c = fixture.componentInstance;

    c.onCommodityChange(usd);
    expect(c.showUnitPrice()).toBe(false);

    c.onCommodityChange(veqt);
    expect(c.showUnitPrice()).toBe(true);
  });

  it('clears the unit-price field when switching from security back to currency', () => {
    const fixture = TestBed.createComponent(AddPositionSheetComponent);
    const c = fixture.componentInstance;

    c.onCommodityChange(veqt);
    c.unitPrice.set('35.12');
    c.onCommodityChange(usd);
    expect(c.unitPrice()).toBe('');
  });

  it('blocks submission until commodity, quantity, and date are valid', () => {
    const fixture = TestBed.createComponent(AddPositionSheetComponent);
    const c = fixture.componentInstance;
    fixture.componentRef.setInput('account', investmentAccount);

    expect(c.canSubmit()).toBe(false);

    c.onCommodityChange(veqt);
    c.quantity.set('100');
    c.asOfDate.set('2026-04-23');
    expect(c.canSubmit()).toBe(true);

    c.unitPrice.set('-1');
    expect(c.canSubmit()).toBe(false);

    c.unitPrice.set('35.12');
    expect(c.canSubmit()).toBe(true);
  });

  it('settles a cash position at the currency minor unit but leaves a share count free-precision', () => {
    const fixture = TestBed.createComponent(AddPositionSheetComponent);
    const c = fixture.componentInstance;

    c.onCommodityChange(veqt);
    expect(c.quantityPrecision()).toBeNull();

    c.onCommodityChange(usd);
    expect(c.quantityPrecision()).toBe(2);
  });
});
