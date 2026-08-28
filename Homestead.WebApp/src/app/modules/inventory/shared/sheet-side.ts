import { DestroyRef, computed, inject, signal, type Signal } from '@angular/core';

/**
 * True below 768px — the exact complement of Tailwind's `md:` at every width — so JS-driven
 * responsive behaviour and `max-md:`/`md:` classes can never disagree. (HlmSidebarService is NOT
 * reused for this: its `(max-width: 768px)` query is inclusive at exactly 768px, which would read
 * "mobile" while the `md:` classes apply — a malformed layout on e.g. iPad portrait.)
 *
 * Must be called in an injection context (it registers a `DestroyRef` teardown for the media-query
 * listener). Environments without matchMedia (jsdom, SSR) get a static `false`.
 */
export function injectIsNarrowViewport(): Signal<boolean> {
  const narrow = signal(false);
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return narrow.asReadonly();
  }
  const query = window.matchMedia('(min-width: 768px)');
  const update = () => narrow.set(!query.matches);
  update();
  query.addEventListener('change', update);
  inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  return narrow.asReadonly();
}

/**
 * Which edge a quick-panel sheet should slide from: bottom sheet on phones (full width, top
 * backdrop band to dismiss), right side sheet otherwise. A thin reading of
 * {@link injectIsNarrowViewport}, so every surface asks the one breakpoint question the same way.
 *
 * Must be called in an injection context. Environments without matchMedia (jsdom, SSR) get a
 * static 'right'.
 */
export function injectSheetSide(): Signal<'bottom' | 'right'> {
  const narrow = injectIsNarrowViewport();
  return computed(() => (narrow() ? 'bottom' : 'right'));
}
