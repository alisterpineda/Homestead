import { DEFAULT_UNIT, resolveUnit } from './consumable-shared';
import { UnitPipe } from './unit.pipe';

describe('resolveUnit', () => {
  it('falls back to the default word for a blank/null/undefined unit', () => {
    expect(resolveUnit('')).toBe(DEFAULT_UNIT);
    expect(resolveUnit('   ')).toBe(DEFAULT_UNIT);
    expect(resolveUnit(null)).toBe(DEFAULT_UNIT);
    expect(resolveUnit(undefined)).toBe(DEFAULT_UNIT);
  });

  it('returns the stored unit when one is set', () => {
    expect(resolveUnit('rolls')).toBe('rolls');
  });

  it('uses "units" as the fallback word', () => {
    expect(DEFAULT_UNIT).toBe('units');
  });
});

describe('UnitPipe', () => {
  const pipe = new UnitPipe();

  it('delegates to resolveUnit', () => {
    expect(pipe.transform(null)).toBe('units');
    expect(pipe.transform('rolls')).toBe('rolls');
    expect(pipe.transform('')).toBe('units');
  });
});
