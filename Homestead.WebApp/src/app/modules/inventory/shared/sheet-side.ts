import { DestroyRef, inject, signal, type Signal } from '@angular/core';

/**
 * Which edge a quick-panel sheet should slide from: bottom sheet on phones (full width, top
 * backdrop band to dismiss), right side sheet otherwise.
 *
 * Watches `(min-width: 768px)` — the exact complement of Tailwind's `max-md:` at every width —
 * so this signal and the panels' `max-md:`/`md:` classes can never disagree. (HlmSidebarService
 * is NOT reused here: its `(max-width: 768px)` query is inclusive at exactly 768px, which would
 * pick the bottom variant while the `md:` classes apply — a malformed sheet on e.g. iPad portrait.)
 *
 * Must be called in an injection context. Environments without matchMedia (jsdom, SSR) get a
 * static 'right'.
 */
export function injectSheetSide(): Signal<'bottom' | 'right'> {
  const side = signal<'bottom' | 'right'>('right');
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return side.asReadonly();
  }
  const query = window.matchMedia('(min-width: 768px)');
  const update = () => side.set(query.matches ? 'right' : 'bottom');
  update();
  query.addEventListener('change', update);
  inject(DestroyRef).onDestroy(() => query.removeEventListener('change', update));
  return side.asReadonly();
}
