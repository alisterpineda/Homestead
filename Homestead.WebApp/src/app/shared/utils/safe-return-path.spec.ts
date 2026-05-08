import { isSafeReturnPath } from './safe-return-path';

describe('isSafeReturnPath', () => {
  it.each([
    ['/documents'],
    ['/'],
    ['/settings'],
    ['/documents/123?tab=tags'],
  ])('accepts safe path %s', (path) => {
    expect(isSafeReturnPath(path)).toBe(true);
  });

  it.each<[string | null | undefined, string]>([
    ['', 'empty string'],
    [null, 'null'],
    [undefined, 'undefined'],
    ['//evil.com/x', 'protocol-relative'],
    ['/\\evil.com', 'backslash trick'],
    ['http://evil.com', 'absolute http URL'],
    ['https://evil.com', 'absolute https URL'],
    ['javascript:alert(1)', 'javascript scheme'],
    ['documents', 'no leading slash'],
    ['./documents', 'relative path'],
  ])('rejects unsafe input %s (%s)', (input) => {
    expect(isSafeReturnPath(input)).toBe(false);
  });
});
