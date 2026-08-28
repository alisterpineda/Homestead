import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { stubMatchMedia } from '@/shared/testing/match-media';
import { MD_BREAKPOINT_QUERY, injectIsNarrowViewport } from './viewport';

describe('injectIsNarrowViewport', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('is false when the viewport is at or above md (48rem)', () => {
    const media = stubMatchMedia(true);
    const narrow = TestBed.runInInjectionContext(() => injectIsNarrowViewport());
    expect(media.matchMedia).toHaveBeenCalledWith(MD_BREAKPOINT_QUERY);
    expect(narrow()).toBe(false);
  });

  it('is true below md', () => {
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
