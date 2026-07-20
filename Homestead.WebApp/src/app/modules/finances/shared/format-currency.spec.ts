import {
  formatCurrency,
  formatMaskedCurrency,
  currencyInputPrecision,
  formatAmountInput,
} from './format-currency';
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

describe('currencyInputPrecision', () => {
  it('returns the display precision for an ISO currency', () => {
    expect(currencyInputPrecision(cad)).toBe(2);
    expect(currencyInputPrecision({ ...cad, symbol: 'JPY', displayPrecision: 0 })).toBe(0);
    expect(currencyInputPrecision({ ...cad, symbol: 'BHD', displayPrecision: 3 })).toBe(3);
  });

  it('returns null for a security, so a share count is never rounded to its display precision', () => {
    expect(currencyInputPrecision(veqt)).toBeNull();
  });

  it('returns null when no commodity has resolved yet', () => {
    expect(currencyInputPrecision(null)).toBeNull();
    expect(currencyInputPrecision(undefined)).toBeNull();
  });

  it('defaults a currency with no declared precision to 2 and never goes negative', () => {
    expect(currencyInputPrecision({ ...cad, displayPrecision: undefined as unknown as number })).toBe(2);
    expect(currencyInputPrecision({ ...cad, displayPrecision: -1 })).toBe(0);
  });
});

describe('formatAmountInput', () => {
  it('pads to the requested precision — the reported "1.2 should read 1.20" case', () => {
    expect(formatAmountInput('1.2', 2)).toBe('1.20');
    expect(formatAmountInput('1', 2)).toBe('1.00');
    expect(formatAmountInput('1.2', 4)).toBe('1.2000');
  });

  it('rounds to a coarser precision', () => {
    expect(formatAmountInput('1.239', 2)).toBe('1.24');
    expect(formatAmountInput('1.231', 2)).toBe('1.23');
    expect(formatAmountInput('1234.5', 0)).toBe('1235');
  });

  it('never renders a signed zero', () => {
    // Rounds to zero but is not itself zero — toFixed alone would emit "-0.00", which reads as a
    // negative amount at a glance.
    expect(formatAmountInput('-0.001', 2)).toBe('0.00');
    expect(formatAmountInput('-0', 2)).toBe('0.00');
    expect(formatAmountInput('0', 2)).toBe('0.00');
  });

  it('leaves the text alone when precision is null (per-unit prices and FX rates)', () => {
    expect(formatAmountInput('1.3745', null)).toBe('1.3745');
    expect(formatAmountInput('1.2', undefined)).toBe('1.2');
  });

  it('keeps an empty field empty rather than materializing 0.00', () => {
    expect(formatAmountInput('', 2)).toBe('');
    expect(formatAmountInput('   ', 2)).toBe('');
  });

  it('returns unparseable text verbatim so the typo stays visible for its owner to fix', () => {
    expect(formatAmountInput('abc', 2)).toBe('abc');
    expect(formatAmountInput('1.2.3', 2)).toBe('1.2.3');
    expect(formatAmountInput('Infinity', 2)).toBe('Infinity');
  });

  it('tolerates surrounding whitespace on an otherwise valid figure', () => {
    expect(formatAmountInput(' 1.2 ', 2)).toBe('1.20');
  });

  it('preserves a negative sign', () => {
    expect(formatAmountInput('-1.2', 2)).toBe('-1.20');
  });
});
