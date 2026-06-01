import { formatNetWorthValue } from './format-net-worth';

describe('formatNetWorthValue', () => {
  it('returns the unmasked formatted value when masked is false', () => {
    const result = formatNetWorthValue(1234.5, 'USD', false, { minFraction: 0, maxFraction: 2 });
    expect(result).toMatch(/\d/);
    expect(result).toMatch(/1.?234/);
  });

  it('produces a magnitude-independent masked string', () => {
    const small = formatNetWorthValue(5, 'USD', true, { minFraction: 0, maxFraction: 2 });
    const huge = formatNetWorthValue(5_000_000.55, 'USD', true, { minFraction: 0, maxFraction: 2 });
    expect(small).toBe(huge);
    expect(small).not.toMatch(/\d/);
    expect(small).not.toMatch(/,/);
  });

  it('masks the headline/tooltip context (maxFraction 2) to one bullet, a decimal mark, and two trailing bullets', () => {
    const result = formatNetWorthValue(5_000_000.55, 'USD', true, { minFraction: 0, maxFraction: 2 });
    const [integerPart, fractionPart] = result.split('.');
    expect(integerPart.match(/•/g)?.length).toBe(1);
    expect(fractionPart).toBeDefined();
    expect(fractionPart.match(/•/g)?.length).toBe(2);
  });

  it('masks the axis context (maxFraction 0) to a single bullet with no decimal mark', () => {
    const result = formatNetWorthValue(5_000_000.55, 'USD', true, { minFraction: 0, maxFraction: 0 });
    expect(result.match(/•/g)?.length).toBe(1);
    expect(result).not.toMatch(/[.,]/);
  });
});
