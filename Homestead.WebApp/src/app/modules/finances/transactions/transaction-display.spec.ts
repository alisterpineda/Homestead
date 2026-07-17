import { shapeLabel, shapeIconName, signedAmountForAccount, transferCounterparty } from './transaction-display';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';

function tx(splits: Array<{ accountId: string; quantity: number; commodityId?: string }>): TransactionResponse {
  return {
    id: 'tx-1',
    memo: '',
    occurredAt: '2026-04-23T00:00:00Z',
    shape: 'Transfer',
    documentLinkCount: 0,
    splits: splits.map((s, i) => ({
      id: `s-${i}`,
      accountId: s.accountId,
      commodityId: s.commodityId ?? 'cad',
      quantity: s.quantity,
    })),
  };
}

describe('shapeLabel', () => {
  it('maps known shapes', () => {
    expect(shapeLabel('Transfer')).toBe('Transfer');
    expect(shapeLabel('BalanceEntry')).toBe('Balance entry');
    expect(shapeLabel('OpeningBalance')).toBe('Opening balance');
    expect(shapeLabel('InvestmentBuy')).toBe('Buy');
    expect(shapeLabel('InvestmentSell')).toBe('Sell');
    expect(shapeLabel('Dividend')).toBe('Dividend');
    expect(shapeLabel('Expense')).toBe('Expense');
    expect(shapeLabel('Income')).toBe('Income');
    expect(shapeLabel('Other')).toBe('Transaction');
  });
});

describe('shapeIconName', () => {
  it('returns a lucide* name for every shape', () => {
    for (const s of [
      'Transfer', 'BalanceEntry', 'OpeningBalance',
      'InvestmentBuy', 'InvestmentSell', 'Dividend', 'Expense', 'Income', 'Other',
    ] as const) {
      expect(shapeIconName(s)).toMatch(/^lucide/);
    }
  });
});

describe('signedAmountForAccount', () => {
  it('sums splits matching the account, ignoring others', () => {
    const t = tx([
      { accountId: 'A', quantity: -100 },
      { accountId: 'B', quantity: 100 },
    ]);
    expect(signedAmountForAccount(t, 'A')).toBe(-100);
    expect(signedAmountForAccount(t, 'B')).toBe(100);
    expect(signedAmountForAccount(t, 'C')).toBe(0);
  });

  it('combines multiple splits on the same account', () => {
    const t = tx([
      { accountId: 'A', quantity: -50 },
      { accountId: 'A', quantity: -30 },
      { accountId: 'B', quantity: 80 },
    ]);
    expect(signedAmountForAccount(t, 'A')).toBe(-80);
  });
});

describe('transferCounterparty', () => {
  it('returns the other leg for a 2-split transfer', () => {
    const t = tx([
      { accountId: 'A', quantity: -100 },
      { accountId: 'B', quantity: 100 },
    ]);
    expect(transferCounterparty(t, 'A')).toEqual({ accountId: 'B', signedAmount: 100 });
    expect(transferCounterparty(t, 'B')).toEqual({ accountId: 'A', signedAmount: -100 });
  });

  it('returns null when no other leg exists', () => {
    const t = tx([{ accountId: 'A', quantity: 0 }]);
    expect(transferCounterparty(t, 'A')).toBeNull();
  });
});
