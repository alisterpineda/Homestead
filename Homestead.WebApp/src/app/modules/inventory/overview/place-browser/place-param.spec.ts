import { placeRefToLink, segmentToLink, placeKey, placeDisplayName } from './place-param';
import type { LocationSegmentResponse } from '@/api/generated/models/location-segment-response';
import type { PlaceContentsResponse } from '@/api/generated/models/place-contents-response';

function makeContents(over: Partial<PlaceContentsResponse>): PlaceContentsResponse {
  return {
    path: [],
    containerItem: null,
    scopedLowStockCount: 0,
    scopedLentCount: 0,
    childAreas: [],
    containers: [],
    items: [],
    consumables: [],
    ...over,
  };
}

describe('place-param', () => {
  describe('placeRefToLink', () => {
    it('maps each ref kind to its browse route — containers drill within Explore', () => {
      expect(placeRefToLink({ kind: 'area', id: 'a1' })).toEqual(['/inventory/browse/area', 'a1']);
      // A container drills within the Browse (Explore) surface, not out to its detail page.
      expect(placeRefToLink({ kind: 'container', id: 'c1' })).toEqual(['/inventory/browse/container', 'c1']);
      expect(placeRefToLink({ kind: 'unplaced' })).toEqual(['/inventory/browse/unplaced']);
    });

    it('maps null to the browse root', () => {
      expect(placeRefToLink(null)).toEqual(['/inventory/browse']);
    });
  });

  describe('segmentToLink', () => {
    it('drills an Area segment in Browse', () => {
      const seg: LocationSegmentResponse = { id: 'garage', name: 'Garage', kind: 'Area' };
      expect(segmentToLink(seg)).toEqual(['/inventory/browse/area', 'garage']);
    });

    it('drills a Container segment in Browse', () => {
      const seg: LocationSegmentResponse = { id: 'chest', name: 'Tool chest', kind: 'Container' };
      expect(segmentToLink(seg)).toEqual(['/inventory/browse/container', 'chest']);
    });

    it('routes an Item segment to its detail page — an assembly is not a place', () => {
      const seg: LocationSegmentResponse = { id: 'pc', name: 'Gaming PC', kind: 'Item' };
      expect(segmentToLink(seg)).toEqual(['/inventory/items', 'pc']);
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
