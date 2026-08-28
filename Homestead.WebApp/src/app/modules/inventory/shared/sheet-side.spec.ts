import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { stubMatchMedia } from '@/shared/testing/match-media';
import { MD_BREAKPOINT_QUERY } from '@/shared/utils/viewport';
import { injectSheetSide } from './sheet-side';

describe('injectSheetSide', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it("is 'right' at or above md (48rem)", () => {
    const media = stubMatchMedia(true);
    const side = TestBed.runInInjectionContext(() => injectSheetSide());
    expect(media.matchMedia).toHaveBeenCalledWith(MD_BREAKPOINT_QUERY);
    expect(side()).toBe('right');
  });

  it("is 'bottom' below md", () => {
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
