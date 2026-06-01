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

  it('produces a magnitude-independent masked string', () => {
    const small = formatMaskedCurrency(5, cad, true, 'en-US');
    const huge = formatMaskedCurrency(5_000_000.55, cad, true, 'en-US');
    expect(small).toBe(huge);
  });

  it('masks to exactly one integer bullet with no grouping separator', () => {
    const result = formatMaskedCurrency(5_000_000.55, cad, true, 'en-US');
    expect(result).not.toMatch(/,/);
    // Exactly one bullet precedes the decimal mark (single integer digit).
    const integerPart = result.split('.')[0];
    expect(integerPart.match(/•/g)?.length).toBe(1);
  });

  it('masks a zero-precision currency (JPY) to a single bullet with no decimal mark', () => {
    const jpy: CommodityResponse = { ...cad, id: 'jpy', symbol: 'JPY', name: 'Japanese Yen', displayPrecision: 0 };
    const result = formatMaskedCurrency(123456, jpy, true, 'en-US');
    expect(result.match(/•/g)?.length).toBe(1);
    expect(result).not.toMatch(/[.,]/);
  });

  it('masks a three-precision currency (BHD) to one bullet, a decimal mark, and three trailing bullets', () => {
    const bhd: CommodityResponse = { ...cad, id: 'bhd', symbol: 'BHD', name: 'Bahraini Dinar', displayPrecision: 3 };
    const result = formatMaskedCurrency(1234.567, bhd, true, 'en-US');
    expect(result).not.toMatch(/,/);
    const [integerPart, fractionPart] = result.split('.');
    expect(integerPart.match(/•/g)?.length).toBe(1);
    expect(fractionPart).toBeDefined();
    expect(fractionPart.match(/•/g)?.length).toBe(3);
  });
});
