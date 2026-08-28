import { DestroyRef, inject, signal, type Signal } from '@angular/core';

/**
 * The literal media query Tailwind v4 emits for `md:` (`--breakpoint-md: 48rem` in
 * `tailwindcss/theme.css`; `src/styles.css` does not override it). Kept as a string so specs and
 * test helpers can assert against the exact question the browser is asked.
 */
export const MD_BREAKPOINT_QUERY = '(width >= 48rem)';

/**
 * True below Tailwind's `md` breakpoint — the one JS-side "is this a phone-sized viewport?"
 * signal for the WebApp.
 *
 * The query is {@link MD_BREAKPOINT_QUERY}, the exact string Tailwind emits for `md:`, so this
 * signal and `md:`/`max-md:` classes can never disagree at any width or root font size (a
 * hard-coded `768px` would drift from `48rem` as soon as the user scales their browser font).
 *
 * Why not `HlmSidebarService.isMobile()`: its `(max-width: 768px)` query is inclusive at exactly
 * 768px, which reads "mobile" while the `md:` classes apply — a malformed layout on e.g. iPad
 * portrait. That service belongs to the sidebar and stays untouched.
 *
 * Why JS at all: CSS can only hide; it cannot choose which component tree to instantiate. Every
 * call site either picks between different trees (popover or right-side sheet vs bottom drawer;
 * resizable panes vs tabs that hold a PDF `<iframe>`, which a CSS-only version would mount and
 * load twice) or derives a *state* default (`'card' | 'table'`) that feeds a persisted
 * preference. So one JS signal is legitimate — and it should stay tiny and ask the browser the
 * literal question Tailwind's `md:` asks.
 *
 * Must be called in an injection context (it registers a `DestroyRef` teardown for the media-query
 * listener). Environments without matchMedia (jsdom, SSR) get a static `false`.
 */
export function injectIsNarrowViewport(): Signal<boolean> {
  const narrow = signal(false);
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return narrow.asReadonly();
  }
  const query = window.matchMedia(MD_BREAKPOINT_QUERY);
  const update = () => narrow.set(!query.matches);
  update();
  query.addEventListener('change', update);
  inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  return narrow.asReadonly();
}
