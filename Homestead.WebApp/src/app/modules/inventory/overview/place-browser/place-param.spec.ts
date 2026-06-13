import { placeRefToLink, segmentToRef, placeKey, placeDisplayName } from './place-param';
import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

function makeContents(over: Partial<PlaceContentsResponse>): PlaceContentsResponse {
  return {
    path: [],
    containerItem: null,
    directItemCount: 0,
    nestedItemCount: 0,
    containers: [],
    items: [],
    supplies: [],
    ...over,
  };
}

describe('place-param', () => {
  describe('placeRefToLink', () => {
    it('maps each ref kind to its browse route', () => {
      expect(placeRefToLink({ kind: 'area', id: 'a1' })).toEqual(['/inventory/browse/area', 'a1']);
      expect(placeRefToLink({ kind: 'container', id: 'c1' })).toEqual(['/inventory/browse/item', 'c1']);
      expect(placeRefToLink({ kind: 'unplaced' })).toEqual(['/inventory/browse/unplaced']);
    });

    it('maps null to the browse root', () => {
      expect(placeRefToLink(null)).toEqual(['/inventory/browse']);
    });
  });

  describe('segmentToRef', () => {
    it('maps an Area segment to an area ref', () => {
      const seg: LocationSegmentResponse = { id: 'garage', name: 'Garage', kind: 'Area' };
      expect(segmentToRef(seg)).toEqual({ kind: 'area', id: 'garage' });
    });

    it('maps a Container segment to a container ref', () => {
      const seg: LocationSegmentResponse = { id: 'chest', name: 'Tool chest', kind: 'Container' };
      expect(segmentToRef(seg)).toEqual({ kind: 'container', id: 'chest' });
    });
  });

  describe('placeKey', () => {
    it('gives a distinct stable key per ref kind, null at root', () => {
      expect(placeKey(null)).toBeNull();
      expect(placeKey({ kind: 'unplaced' })).toBe('unplaced');
      expect(placeKey({ kind: 'area', id: 'a1' })).toBe('area:a1');
      expect(placeKey({ kind: 'container', id: 'c1' })).toBe('container:c1');
      // An area and a container with the same id must not collide.
      expect(placeKey({ kind: 'area', id: 'x' })).not.toBe(placeKey({ kind: 'container', id: 'x' }));
    });
  });

  describe('placeDisplayName', () => {
    it('is empty at the root', () => {
      expect(placeDisplayName(null, null)).toBe('');
    });

    it('labels Unplaced without needing contents', () => {
      expect(placeDisplayName({ kind: 'unplaced' }, null)).toBe('Unplaced');
    });

    it('uses the last path segment when the path is loaded', () => {
      const contents = makeContents({
        path: [
          { id: 'garage', name: 'Garage', kind: 'Area' },
          { id: 'chest', name: 'Tool chest', kind: 'Container' },
        ],
      });
      expect(placeDisplayName({ kind: 'container', id: 'chest' }, contents)).toBe('Tool chest');
    });

    it('falls back to the drilled container name before the path loads', () => {
      const contents = makeContents({
        path: [],
        containerItem: {
          id: 'chest', name: 'Tool chest', fungibility: 'NonFungible', status: 'Active',
          isContainer: true, path: [], createdAt: '', updatedAt: '',
        },
      });
      expect(placeDisplayName({ kind: 'container', id: 'chest' }, contents)).toBe('Tool chest');
    });

    it('is empty for a drilled place with no path and no contents yet', () => {
      expect(placeDisplayName({ kind: 'area', id: 'garage' }, null)).toBe('');
    });
  });
});
