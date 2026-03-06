import { parseDate, toIsoDateString, datesEqual, handleFileInputChange } from './utils';

describe('parseDate', () => {
  it('should return null for null/undefined/empty', () => {
    expect(parseDate(null)).toBeNull();
    expect(parseDate(undefined)).toBeNull();
    expect(parseDate('')).toBeNull();
  });

  it('should parse an ISO date string to a local Date', () => {
    const result = parseDate('2026-03-04');
    expect(result).toBeInstanceOf(Date);
    expect(result!.getFullYear()).toBe(2026);
    expect(result!.getMonth()).toBe(2); // March = 2
    expect(result!.getDate()).toBe(4);
  });

  it('should handle full ISO datetime by taking only the date part', () => {
    const result = parseDate('2026-12-25T10:30:00Z');
    expect(result!.getFullYear()).toBe(2026);
    expect(result!.getMonth()).toBe(11);
    expect(result!.getDate()).toBe(25);
  });
});

describe('toIsoDateString', () => {
  it('should return null for null', () => {
    expect(toIsoDateString(null)).toBeNull();
  });

  it('should format a Date as YYYY-MM-DD', () => {
    expect(toIsoDateString(new Date(2026, 2, 4))).toBe('2026-03-04');
  });

  it('should zero-pad month and day', () => {
    expect(toIsoDateString(new Date(2026, 0, 1))).toBe('2026-01-01');
  });
});

describe('datesEqual', () => {
  it('should return true for two nulls', () => {
    expect(datesEqual(null, null)).toBe(true);
  });

  it('should return false when one is null', () => {
    expect(datesEqual(new Date(2026, 0, 1), null)).toBe(false);
    expect(datesEqual(null, new Date(2026, 0, 1))).toBe(false);
  });

  it('should return true for same date', () => {
    expect(datesEqual(new Date(2026, 2, 4), new Date(2026, 2, 4))).toBe(true);
  });

  it('should return false for different dates', () => {
    expect(datesEqual(new Date(2026, 2, 4), new Date(2026, 2, 5))).toBe(false);
  });

  it('should ignore time differences', () => {
    const a = new Date(2026, 2, 4, 10, 30);
    const b = new Date(2026, 2, 4, 22, 0);
    expect(datesEqual(a, b)).toBe(true);
  });
});

describe('handleFileInputChange', () => {
  it('should call onFiles with selected files and reset input', () => {
    const file = new File(['content'], 'test.pdf');
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    const onFiles = vi.fn();

    handleFileInputChange({ target: input } as unknown as Event, onFiles);

    expect(onFiles).toHaveBeenCalledWith([file]);
    expect(input.value).toBe('');
  });

  it('should not call onFiles when no files selected', () => {
    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [], configurable: true });
    const onFiles = vi.fn();

    handleFileInputChange({ target: input } as unknown as Event, onFiles);

    expect(onFiles).not.toHaveBeenCalled();
  });
});
