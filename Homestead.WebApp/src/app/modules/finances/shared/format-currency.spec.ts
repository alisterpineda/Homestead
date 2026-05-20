import { formatCurrency, formatMaskedCurrency } from './format-currency';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

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

describe('formatMaskedCurrency', () => {
  it('returns the unmasked formatted value when masked is false', () => {
    expect(formatMaskedCurrency(1234.5, cad, false, 'en-US')).toBe(formatCurrency(1234.5, cad, 'en-US'));
  });

  it('masks digits while preserving the currency symbol and separators for ISO currencies', () => {
    const result = formatMaskedCurrency(1234.5, cad, true, 'en-US');
    // The locale-resolved symbol can be "CA$", "CAD", or just "$"; the digits/decimal/grouping
    // shape is what we lock down here. Assert: at least one bullet, no digit, and a decimal mark.
    expect(result).toMatch(/•/);
    expect(result).not.toMatch(/\d/);
    expect(result).toMatch(/[.,]/);
  });

  it('masks digits and keeps the trailing security symbol for non-currency commodities', () => {
    const result = formatMaskedCurrency(100, veqt, true, 'en-US');
    expect(result.endsWith(' VEQT')).toBe(true);
    expect(result).toMatch(/•/);
    expect(result.replace(' VEQT', '')).not.toMatch(/\d/);
  });

  it('masks zero values without revealing them', () => {
    const result = formatMaskedCurrency(0, cad, true, 'en-US');
    expect(result).not.toMatch(/0/);
    expect(result).toMatch(/•/);
  });

  it('strips the minus sign so masked output is constant-width for negative values', () => {
    const masked = formatMaskedCurrency(-500, cad, true, 'en-US');
    expect(masked).not.toMatch(/[-−]/);
    expect(masked).not.toMatch(/\d/);
  });

  it('produces the same masked string for equal magnitudes regardless of sign', () => {
    const pos = formatMaskedCurrency(500, cad, true, 'en-US');
    const neg = formatMaskedCurrency(-500, cad, true, 'en-US');
    expect(pos).toBe(neg);
  });
});
