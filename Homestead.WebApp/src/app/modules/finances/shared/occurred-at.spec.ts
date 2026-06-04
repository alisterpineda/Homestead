import { composeOccurredAt, decomposeOccurredAt } from './occurred-at';

describe('composeOccurredAt', () => {
  it('anchors a date-only entry (null time) to noon UTC', () => {
    expect(composeOccurredAt('2026-04-23', null)).toBe('2026-04-23T12:00:00.000Z');
  });

  it('converts a local wall-clock date+time to a UTC instant', () => {
    // Build the expected instant via the same Date math the helper uses, so the
    // assertion is timezone-agnostic (CI may run in any TZ).
    const expected = new Date(2026, 3, 23, 9, 30).toISOString();
    expect(composeOccurredAt('2026-04-23', '09:30')).toBe(expected);
  });
});

describe('decomposeOccurredAt', () => {
  it('collapses a noon-UTC instant to date-only (time = null)', () => {
    expect(decomposeOccurredAt('2026-04-23T12:00:00.000Z')).toEqual({ date: '2026-04-23', time: null });
  });

  it('keeps the UTC calendar date when collapsing, irrespective of local TZ', () => {
    // Noon UTC never crosses a date boundary for any real-world offset, so the
    // collapsed date is the UTC date in the ISO string.
    expect(decomposeOccurredAt('2026-12-31T12:00:00.000Z').date).toBe('2026-12-31');
  });

  it('expands a non-noon instant to local date + HH:mm', () => {
    const iso = new Date(2026, 3, 23, 9, 30).toISOString();
    expect(decomposeOccurredAt(iso)).toEqual({ date: '2026-04-23', time: '09:30' });
  });
});

describe('compose ∘ decompose round-trip', () => {
  it('round-trips the noon-UTC default', () => {
    const iso = composeOccurredAt('2026-04-23', null);
    const { date, time } = decomposeOccurredAt(iso);
    expect(date).toBe('2026-04-23');
    expect(time).toBeNull();
    expect(composeOccurredAt(date, time)).toBe(iso);
  });

  it('round-trips an expanded local time', () => {
    const iso = composeOccurredAt('2026-04-23', '14:05');
    const { date, time } = decomposeOccurredAt(iso);
    expect(composeOccurredAt(date, time)).toBe(iso);
  });

  // Accepted v1 limitation (see occurred-at.ts): an explicit local time that lands
  // exactly on noon UTC re-seeds collapsed (display-only), but the stored INSTANT is
  // preserved — re-composing the collapsed fields reproduces the same instant, so no
  // value is lost. Pin that invariant rather than the (lossy) field display.
  it('preserves the instant when an explicit time collides with the noon-UTC sentinel', () => {
    const localNoonAtUtcNoon = new Date(2026, 3, 23, 12, 0).toISOString();
    if (localNoonAtUtcNoon !== '2026-04-23T12:00:00.000Z') {
      // CI is not at UTC offset 0; the collision can't be constructed here. The
      // invariant under test is offset-independent, so the round-trip below still holds.
      return;
    }
    const { date, time } = decomposeOccurredAt(localNoonAtUtcNoon);
    expect(time).toBeNull(); // collapses (display-only)
    expect(composeOccurredAt(date, time)).toBe(localNoonAtUtcNoon); // instant preserved
  });
});
