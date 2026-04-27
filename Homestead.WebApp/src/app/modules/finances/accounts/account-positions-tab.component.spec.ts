import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideZonelessChangeDetection } from '@angular/core';
import { AccountPositionsTabComponent } from './account-positions-tab.component';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { PositionResponse } from '@/api/generated/models/position-response';
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

class FakeSettings {
  displayCurrency = () => cad;
}

describe('AccountPositionsTabComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        { provide: FinanceSettingsService, useClass: FakeSettings },
      ],
    });
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(AccountPositionsTabComponent);
    fixture.componentRef.setInput('account', investmentAccount);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('formats quantity with the commodity precision and shows display value via display currency', () => {
    const fixture = TestBed.createComponent(AccountPositionsTabComponent);
    fixture.componentRef.setInput('account', investmentAccount);
    const c = fixture.componentInstance;

    const positions: PositionResponse[] = [
      { commodityId: 'veqt', quantity: 100, displayValue: 3512.0, shareOfAccount: 0.7 },
      { commodityId: 'cad', quantity: 1500, displayValue: 1500, shareOfAccount: 0.3 },
    ];
    c.raw.set(positions);
    c.commodities.set({ veqt, cad });

    const rows = c.rows();
    expect(rows.length).toBe(2);
    expect(rows[0].quantityText).toContain('100.0000');
    expect(rows[0].quantityText).toContain('VEQT');
    expect(rows[0].displayValueText).toContain('3,512');
    expect(rows[0].shareText).toBe('70.0%');
    expect(rows[1].displayValueText).toContain('1,500');
  });

  it('renders unpriced positions with no display value and no share', () => {
    const fixture = TestBed.createComponent(AccountPositionsTabComponent);
    fixture.componentRef.setInput('account', investmentAccount);
    const c = fixture.componentInstance;

    c.raw.set([
      { commodityId: 'veqt', quantity: 50, displayValue: null, shareOfAccount: null },
    ]);
    c.commodities.set({ veqt });

    const [row] = c.rows();
    expect(row.displayValueText).toBeNull();
    expect(row.shareText).toBeNull();
    expect(c.unpricedCount()).toBe(1);
  });

  it('emits commodityId when edit-opening-balance is invoked', () => {
    const fixture = TestBed.createComponent(AccountPositionsTabComponent);
    fixture.componentRef.setInput('account', investmentAccount);
    const c = fixture.componentInstance;

    let emitted: { commodityId: string } | null = null;
    c.editOpeningBalance.subscribe(e => { emitted = e; });

    c.onEditOpeningBalance('veqt');
    expect(emitted).toEqual({ commodityId: 'veqt' });
  });
});
