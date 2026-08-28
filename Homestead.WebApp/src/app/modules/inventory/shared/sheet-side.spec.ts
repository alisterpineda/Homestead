import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { injectIsNarrowViewport, injectSheetSide } from './sheet-side';

interface FakeMediaQueryList {
  matches: boolean;
  addEventListener: ReturnType<typeof vi.fn>;
  removeEventListener: ReturnType<typeof vi.fn>;
}

/** Stubs window.matchMedia and hands back the list plus a way to fire `change`. */
function stubMatchMedia(matches: boolean) {
  const listeners: (() => void)[] = [];
  const query: FakeMediaQueryList = {
    matches,
    addEventListener: vi.fn((_type: string, fn: () => void) => listeners.push(fn)),
    removeEventListener: vi.fn(),
  };
  const matchMedia = vi.fn(() => query);
  vi.stubGlobal('matchMedia', matchMedia);
  return {
    query,
    matchMedia,
    fireChange(next: boolean) {
      query.matches = next;
      for (const fn of [...listeners]) fn();
    },
  };
}

describe('sheet-side', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => vi.unstubAllGlobals());

  describe('injectIsNarrowViewport', () => {
    it('is false when the viewport is at least 768px wide', () => {
      const media = stubMatchMedia(true);
      const narrow = TestBed.runInInjectionContext(() => injectIsNarrowViewport());
      expect(media.matchMedia).toHaveBeenCalledWith('(min-width: 768px)');
      expect(narrow()).toBe(false);
    });

    it('is true below 768px', () => {
      stubMatchMedia(false);
      const narrow = TestBed.runInInjectionContext(() => injectIsNarrowViewport());
      expect(narrow()).toBe(true);
    });

    it('flips when the media query fires a change', () => {
      const media = stubMatchMedia(true);
      const narrow = TestBed.runInInjectionContext(() => injectIsNarrowViewport());
      expect(narrow()).toBe(false);
      media.fireChange(false);
      expect(narrow()).toBe(true);
      media.fireChange(true);
      expect(narrow()).toBe(false);
    });

    it('removes the change listener on destroy', () => {
      const media = stubMatchMedia(true);
      TestBed.runInInjectionContext(() => injectIsNarrowViewport());
      const registered = media.query.addEventListener.mock.calls[0];
      expect(media.query.removeEventListener).not.toHaveBeenCalled();
      TestBed.resetTestingModule();
      expect(media.query.removeEventListener).toHaveBeenCalledWith('change', registered[1]);
    });

    it('is a static false without matchMedia (jsdom/SSR)', () => {
      vi.stubGlobal('matchMedia', undefined);
      const narrow = TestBed.runInInjectionContext(() => injectIsNarrowViewport());
      expect(narrow()).toBe(false);
    });
  });

  describe('injectSheetSide', () => {
    it("is 'right' at or above 768px", () => {
      const media = stubMatchMedia(true);
      const side = TestBed.runInInjectionContext(() => injectSheetSide());
      expect(media.matchMedia).toHaveBeenCalledWith('(min-width: 768px)');
      expect(side()).toBe('right');
    });

    it("is 'bottom' below 768px", () => {
      stubMatchMedia(false);
      const side = TestBed.runInInjectionContext(() => injectSheetSide());
      expect(side()).toBe('bottom');
    });

    it('flips when the media query fires a change', () => {
      const media = stubMatchMedia(true);
      const side = TestBed.runInInjectionContext(() => injectSheetSide());
      expect(side()).toBe('right');
      media.fireChange(false);
      expect(side()).toBe('bottom');
      media.fireChange(true);
      expect(side()).toBe('right');
    });

    it('removes the change listener on destroy', () => {
      const media = stubMatchMedia(true);
      TestBed.runInInjectionContext(() => injectSheetSide());
      const registered = media.query.addEventListener.mock.calls[0];
      TestBed.resetTestingModule();
      expect(media.query.removeEventListener).toHaveBeenCalledWith('change', registered[1]);
    });

    it("is a static 'right' without matchMedia (jsdom/SSR)", () => {
      vi.stubGlobal('matchMedia', undefined);
      const side = TestBed.runInInjectionContext(() => injectSheetSide());
      expect(side()).toBe('right');
    });
  });
});
