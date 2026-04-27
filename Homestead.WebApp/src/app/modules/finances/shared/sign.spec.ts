import { displaySign, toUserAmount, netWorth, formatAccountBalance } from './sign';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const cad: CommodityResponse = {
  id: 'cad',
  symbol: 'CAD',
  exchange: null,
  name: 'Canadian Dollar',
  kind: 'Currency',
  displayPrecision: 2,
  isSystemManaged: true,
};

function account(overrides: Partial<AccountResponse>): AccountResponse {
  return {
    id: 'a',
    name: 'A',
    accountType: 'Asset',
    kind: 'Cash',
    reportingCurrencyId: 'cad',
    institution: null,
    isSystem: false,
    isArchived: false,
    createdAt: '',
    updatedAt: '',
    displayValue: 0,
    nativeBalance: 0,
    unpricedHoldingCount: 0,
    ...overrides,
  };
}

describe('displaySign', () => {
  it('returns -1 for liabilities', () => {
    expect(displaySign('Liability')).toBe(-1);
  });
  it('returns 1 for assets and others', () => {
    expect(displaySign('Asset')).toBe(1);
    expect(displaySign('Equity')).toBe(1);
    expect(displaySign('Income')).toBe(1);
    expect(displaySign('Expense')).toBe(1);
  });
});

describe('toUserAmount', () => {
  it('flips liability sign so amount-owed is positive', () => {
    expect(toUserAmount(-500, 'Liability')).toBe(500);
  });
  it('leaves asset amounts unchanged', () => {
    expect(toUserAmount(1000, 'Asset')).toBe(1000);
  });
  it('returns null for null', () => {
    expect(toUserAmount(null, 'Asset')).toBeNull();
  });
});

describe('netWorth', () => {
  it('sums displayValue across accounts (Beancount sign collapses by itself)', () => {
    expect(netWorth([
      account({ accountType: 'Asset', displayValue: 1000 }),
      account({ accountType: 'Liability', displayValue: -500 }),
    ])).toBe(500);
  });
  it('skips unpriced (null displayValue) holdings', () => {
    expect(netWorth([
      account({ displayValue: 1000 }),
      account({ displayValue: null }),
    ])).toBe(1000);
  });
});

describe('formatAccountBalance', () => {
  it('returns null when display currency is missing', () => {
    expect(formatAccountBalance(account({ displayValue: 100 }), null)).toBeNull();
  });
  it('returns null when displayValue is null (unpriced)', () => {
    expect(formatAccountBalance(account({ displayValue: null }), cad)).toBeNull();
  });
  it('flips liability sign in the formatted output', () => {
    const result = formatAccountBalance(
      account({ accountType: 'Liability', displayValue: -500 }),
      cad,
    );
    expect(result).toBeTruthy();
    expect(result).not.toContain('-');
  });
});
