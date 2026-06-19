import { formatRelativeAge } from './relative-age';

describe('formatRelativeAge', () => {
  const now = Date.now();
  const agoDays = (days: number): string => new Date(now - days * 24 * 60 * 60 * 1000).toISOString();
  const agoMinutes = (min: number): string => new Date(now - min * 60 * 1000).toISOString();

  it('reports recent activity in seconds/minutes/hours/days', () => {
    expect(formatRelativeAge(agoMinutes(0))).toBe('just now');
    expect(formatRelativeAge(agoMinutes(5))).toBe('5m ago');
    expect(formatRelativeAge(agoMinutes(150))).toBe('2h ago');
    expect(formatRelativeAge(agoDays(3))).toBe('3d ago');
  });

  it('reports months for sub-year ages', () => {
    expect(formatRelativeAge(agoDays(35))).toBe('1mo ago');
    expect(formatRelativeAge(agoDays(359))).toBe('11mo ago');
  });

  // Regression: the months branch caps at diffMo==12 (360 days) but years used /365, so ages of
  // 360–364 days fell through to "0y ago". Years are now derived from the 30-day month unit.
  it('never renders "0y ago" at the month/year boundary', () => {
    expect(formatRelativeAge(agoDays(360))).toBe('1y ago');
    expect(formatRelativeAge(agoDays(362))).toBe('1y ago');
    expect(formatRelativeAge(agoDays(365))).toBe('1y ago');
    expect(formatRelativeAge(agoDays(800))).toBe('2y ago');
  });

  it('falls back to a locale date for future timestamps', () => {
    const future = new Date(now + 24 * 60 * 60 * 1000).toISOString();
    expect(formatRelativeAge(future)).toBe(new Date(future).toLocaleDateString());
  });
});
