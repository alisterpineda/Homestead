import { EXPIRY_PRESETS, addYearsIso, presetLabel } from './warranty-expiry';

describe('addYearsIso', () => {
  it('adds whole calendar years', () => {
    expect(addYearsIso('2026-03-12', 1)).toBe('2027-03-12');
    expect(addYearsIso('2026-03-12', 5)).toBe('2031-03-12');
  });

  it('clamps 29 February to the 28th rather than rolling into March', () => {
    expect(addYearsIso('2024-02-29', 1)).toBe('2025-02-28');
    expect(addYearsIso('2024-02-29', 3)).toBe('2027-02-28');
    // A leap year four out still has the 29th.
    expect(addYearsIso('2024-02-29', 4)).toBe('2028-02-29');
  });

  it('ignores any time component on the base', () => {
    expect(addYearsIso('2026-03-12T09:15:00Z', 2)).toBe('2028-03-12');
  });

  it('offers 1/2/3/5-year presets', () => {
    expect(EXPIRY_PRESETS).toEqual([1, 2, 3, 5]);
  });
});

describe('presetLabel', () => {
  it('pairs the length with the date it resolves to', () => {
    expect(presetLabel(2, '2028-03-12', 'en-US')).toBe('2 years · Mar 12, 2028');
  });

  it('singularises one year', () => {
    expect(presetLabel(1, '2027-03-12', 'en-US')).toBe('1 year · Mar 12, 2027');
  });
});
