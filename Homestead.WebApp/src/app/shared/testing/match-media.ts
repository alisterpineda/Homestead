// Test-only module: excluded from the app build in tsconfig.app.json (uses the vitest `vi` global).
/**
 * Test-only stub for `window.matchMedia`, for specs exercising `injectIsNarrowViewport()`
 * (`@/shared/utils/viewport`) or components built on it.
 *
 * Call it **before** `TestBed.createComponent` / `runInInjectionContext` — the helper reads
 * `matchMedia` at construction — and `vi.unstubAllGlobals()` in `afterEach`.
 *
 * `matches` is what the query reports, i.e. `true` = at or above `md` = desktop.
 */
export interface FakeMediaQueryList {
  matches: boolean;
  addEventListener: ReturnType<typeof vi.fn>;
  removeEventListener: ReturnType<typeof vi.fn>;
}

export function stubMatchMedia(matches: boolean) {
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
    /** Flip what the query reports and notify every registered `change` listener. */
    fireChange(next: boolean) {
      query.matches = next;
      for (const fn of [...listeners]) fn();
    },
  };
}
