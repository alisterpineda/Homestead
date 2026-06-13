import { parsePlaceParam, formatPlaceParam, type PlaceRef } from './place-param';

describe('place-param', () => {
  it('parses the prefixed forms', () => {
    expect(parsePlaceParam('area:a1')).toEqual({ kind: 'area', id: 'a1' });
    expect(parsePlaceParam('item:c1')).toEqual({ kind: 'container', id: 'c1' });
    expect(parsePlaceParam('unplaced')).toEqual({ kind: 'unplaced' });
  });

  it('degrades absent or malformed values to root', () => {
    expect(parsePlaceParam(null)).toBeNull();
    expect(parsePlaceParam('')).toBeNull();
    expect(parsePlaceParam('area:')).toBeNull();
    expect(parsePlaceParam('item:')).toBeNull();
    expect(parsePlaceParam('bogus')).toBeNull();
    expect(parsePlaceParam('container:c1')).toBeNull();
    expect(parsePlaceParam('Unplaced')).toBeNull();
  });

  it('formats each ref kind and null', () => {
    expect(formatPlaceParam({ kind: 'area', id: 'a1' })).toBe('area:a1');
    expect(formatPlaceParam({ kind: 'container', id: 'c1' })).toBe('item:c1');
    expect(formatPlaceParam({ kind: 'unplaced' })).toBe('unplaced');
    expect(formatPlaceParam(null)).toBeNull();
  });

  it('round-trips every ref through format → parse', () => {
    const refs: PlaceRef[] = [
      { kind: 'area', id: 'a1' },
      { kind: 'container', id: 'c1' },
      { kind: 'unplaced' },
      null,
    ];
    for (const ref of refs) {
      expect(parsePlaceParam(formatPlaceParam(ref))).toEqual(ref);
    }
  });
});
