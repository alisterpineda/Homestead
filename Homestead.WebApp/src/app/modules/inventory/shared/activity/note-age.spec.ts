import { formatDate } from '@angular/common';
import { formatNoteAge } from './note-age';

const DAY_MS = 24 * 60 * 60 * 1000;

describe('formatNoteAge', () => {
  it('renders a recent instant relative', () => {
    const twoDaysAgo = new Date(Date.now() - 2 * DAY_MS).toISOString();
    expect(formatNoteAge(twoDaysAgo, 'en-US')).toBe('2d ago');
  });

  it('renders anything beyond a week as an absolute date', () => {
    const tenDaysAgo = new Date(Date.now() - 10 * DAY_MS).toISOString();
    expect(formatNoteAge(tenDaysAgo, 'en-US')).toBe(formatDate(tenDaysAgo, 'mediumDate', 'en-US'));
  });

  it('keeps a back-filled entry from reading as "just now"', () => {
    expect(formatNoteAge('2019-06-14T12:00:00.000Z', 'en-US')).not.toContain('ago');
  });

  it('renders nothing for an unparseable or missing timestamp', () => {
    expect(formatNoteAge('not-a-date', 'en-US')).toBe('');
    expect(formatNoteAge(null, 'en-US')).toBe('');
    expect(formatNoteAge(undefined, 'en-US')).toBe('');
  });

  it('treats a small clock skew as "just now", not a future date', () => {
    const slightlyAhead = new Date(Date.now() + 5_000).toISOString();
    expect(formatNoteAge(slightlyAhead, 'en-US')).toBe('just now');
  });
});
