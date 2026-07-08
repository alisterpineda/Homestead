import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { openGeneration } from './open-generation';

describe('openGeneration', () => {
  it('starts at 0 before any open transition', () => {
    TestBed.runInInjectionContext(() => {
      const state = signal<BrnDialogState>('closed');
      const gen = openGeneration(state);
      TestBed.flushEffects();
      expect(gen()).toBe(0);
    });
  });

  it('increments each time state transitions to open', () => {
    TestBed.runInInjectionContext(() => {
      const state = signal<BrnDialogState>('closed');
      const gen = openGeneration(state);
      TestBed.flushEffects();

      state.set('open');
      TestBed.flushEffects();
      expect(gen()).toBe(1);

      state.set('closed');
      TestBed.flushEffects();
      expect(gen()).toBe(1); // no bump on close

      state.set('open');
      TestBed.flushEffects();
      expect(gen()).toBe(2); // reopen bumps again
    });
  });

  it('does not increment on a close-only transition', () => {
    TestBed.runInInjectionContext(() => {
      const state = signal<BrnDialogState>('open');
      const gen = openGeneration(state);
      TestBed.flushEffects();
      expect(gen()).toBe(1); // initial value is already 'open'

      state.set('closed');
      TestBed.flushEffects();
      expect(gen()).toBe(1);
    });
  });
});
