import { needsClamp } from './note-body';

describe('needsClamp', () => {
  it('leaves a short single-line body unclamped', () => {
    expect(needsClamp('Changed the oil.')).toBe(false);
  });

  it('leaves exactly three lines unclamped', () => {
    expect(needsClamp('one\ntwo\nthree')).toBe(false);
  });

  it('clamps a fourth line', () => {
    expect(needsClamp('one\ntwo\nthree\nfour')).toBe(true);
  });

  it('clamps a long single-line body past 240 characters', () => {
    expect(needsClamp('x'.repeat(240))).toBe(false);
    expect(needsClamp('x'.repeat(241))).toBe(true);
  });

  it('treats an empty or missing body as unclamped', () => {
    expect(needsClamp('')).toBe(false);
    expect(needsClamp(null)).toBe(false);
    expect(needsClamp(undefined)).toBe(false);
  });
});
