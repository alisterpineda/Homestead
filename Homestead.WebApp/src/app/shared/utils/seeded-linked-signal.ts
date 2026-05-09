import { linkedSignal, untracked, type Signal, type WritableSignal } from '@angular/core';

/**
 * Wraps `linkedSignal({ source, computation })` so the computation runs in an
 * `untracked` context. Without this wrapper, signals read inside the computation
 * become tracked dependencies and cause the linked signal to recompute on
 * unrelated emits — silently overwriting user-edited drafts.
 *
 * Use for writable form-field signals that should reset only when a primitive
 * source key (e.g. an entity id) changes.
 */
export function seededLinkedSignal<S, V>(
  source: Signal<S>,
  fn: () => V,
): WritableSignal<V> {
  return linkedSignal<S, V>({ source, computation: () => untracked(fn) });
}
