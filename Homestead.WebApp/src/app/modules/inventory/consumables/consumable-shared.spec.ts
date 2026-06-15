import { isWholeCount, formatQty } from './consumable-shared';

describe('isWholeCount', () => {
  it('is true for whole numbers at least 1', () => {
    expect(isWholeCount(1)).toBe(true);
    expect(isWholeCount(5)).toBe(true);
    expect(isWholeCount(120)).toBe(true);
  });

  it('is false for fractional quantities', () => {
    expect(isWholeCount(3.5)).toBe(false);
    expect(isWholeCount(0.5)).toBe(false);
    expect(isWholeCount(1.0001)).toBe(false);
  });

  it('is false below 1', () => {
    expect(isWholeCount(0)).toBe(false);
    expect(isWholeCount(-1)).toBe(false);
  });
});

describe('formatQty', () => {
  it('trims trailing zeros', () => {
    expect(formatQty(2.5)).toBe('2.5');
    expect(formatQty(12)).toBe('12');
  });
});
