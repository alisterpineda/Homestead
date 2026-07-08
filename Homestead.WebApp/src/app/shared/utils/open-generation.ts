import { effect, signal, untracked, type Signal } from '@angular/core';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

/**
 * A monotonically increasing "open generation" that bumps each time `state` transitions to 'open'.
 * Fold into a seededLinkedSignal seed key so closing and reopening the same entity's form reseeds
 * the draft. A plain `state()==='open'` boolean round-trips to an identical key, and because the
 * form fields are only read inside a portal that is destroyed while closed, the lazy linkedSignal
 * never observes the intermediate value and keeps the stale draft. Only an eager effect (not a
 * computed) sees the close→open transition while nothing downstream is being read.
 *
 * Call in an injection context (component field initializer); the effect auto-destroys with it.
 */
export function openGeneration(state: Signal<BrnDialogState>): Signal<number> {
  const gen = signal(0);
  effect(() => {
    if (state() === 'open') untracked(() => gen.update((n) => n + 1));
  });
  return gen.asReadonly();
}
