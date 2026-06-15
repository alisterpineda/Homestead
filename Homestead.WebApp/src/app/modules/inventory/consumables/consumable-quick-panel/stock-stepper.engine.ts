import { signal, computed, type Signal } from '@angular/core';
import { type Observable } from 'rxjs';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';

/** A stock location identified by its area/container pair (stable across entry delete/recreate). */
export function stockLocationKey(loc: { areaId?: string | null; containerItemId?: string | null }): string {
  return `${loc.areaId ?? ''}|${loc.containerItemId ?? ''}`;
}

/**
 * Hooks parameterised on a payload captured at tap time. The payload — not component state read
 * later — identifies what a commit acts on, so jobs that run after the panel has moved to another
 * entity still commit against the right one.
 */
export interface StepperHooks<P> {
  /** Commits the net delta for a burst as one Flow movement (consume or receive). */
  commit(payload: P, delta: number): Observable<ConsumableDetailResponse>;
  /** A stepper commit succeeded — the response is the full truth for the payload's entity. */
  onCommitted(detail: ConsumableDetailResponse): void;
  /** A commit failed — local optimistic state for the key has been dropped; resync. */
  onError(payload: P, err: unknown): void;
}

interface PendingBurst<P> {
  delta: number;
  payload: P;
}

interface CommitJob {
  /** Set on stepper commits so a failed key can purge its queued successors. */
  stepperKey?: string;
  stepperDelta?: number;
  run(): Observable<unknown>;
  onNext(result: unknown): void;
  onError(err: unknown): void;
}

/**
 * Debounces − / + stepper taps into net-delta commits and serializes ALL panel mutations through
 * one single-flight FIFO queue, so each response can safely overwrite the panel's detail state.
 *
 * Plain class (no DI) so it is fake-timer testable. Intentionally NOT teardown-bound: a flush
 * triggered by closing the sheet must still complete after the component's portal is destroyed —
 * the hooks only set signals/emit outputs, which is safe on a live component instance.
 */
export class StockStepperEngine<P> {
  private readonly _pending = signal<ReadonlyMap<string, PendingBurst<P>>>(new Map());
  /** Net delta per key across queued + running stepper commits — keeps the display optimistic. */
  private readonly _committing = signal<ReadonlyMap<string, number>>(new Map());
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly queue: CommitJob[] = [];
  private idleResolvers: (() => void)[] = [];
  private running = false;

  constructor(
    private readonly hooks: StepperHooks<P>,
    private readonly debounceMs = 800,
  ) {}

  /** Net optimistic delta (pending burst + queued/running commits) for one location key. */
  deltaFor(key: string): number {
    return (this._pending().get(key)?.delta ?? 0) + (this._committing().get(key) ?? 0);
  }

  /** Net optimistic delta across all locations — keeps the total in step with the steppers. */
  readonly totalDelta: Signal<number> = computed(() => {
    let total = 0;
    for (const burst of this._pending().values()) total += burst.delta;
    for (const delta of this._committing().values()) total += delta;
    return total;
  });

  readonly isBusy: Signal<boolean> = computed(
    () => this._pending().size > 0 || this._committing().size > 0,
  );

  /** Resolves once nothing is pending, queued, or in flight. */
  whenIdle(): Promise<void> {
    if (!this.hasWork()) return Promise.resolve();
    return new Promise((resolve) => this.idleResolvers.push(resolve));
  }

  /**
   * Adjusts the pending burst for a key and (re)starts its debounce timer. The payload is captured
   * here — at user-intent time — and travels with the burst into its commit job.
   */
  tap(key: string, delta: number, payload: P): void {
    const next = new Map(this._pending());
    const updated = (next.get(key)?.delta ?? 0) + delta;
    this.cancelTimer(key);
    if (updated === 0) {
      // Net-zero burst — drop it entirely, no API call.
      next.delete(key);
      this._pending.set(next);
      this.settleIfIdle();
      return;
    }
    next.set(key, { delta: updated, payload });
    this._pending.set(next);
    this.timers.set(key, setTimeout(() => this.flush(key), this.debounceMs));
  }

  /** Commits a key's pending burst now (e.g. before opening an inline form for it). */
  flush(key: string): void {
    this.cancelTimer(key);
    const burst = this._pending().get(key);
    if (!burst) return;
    const next = new Map(this._pending());
    next.delete(key);
    this._pending.set(next);
    // Counted as committing from queue time so the optimistic display never dips while queued.
    this.addCommitting(key, burst.delta);

    this.queue.push({
      stepperKey: key,
      stepperDelta: burst.delta,
      run: () => this.hooks.commit(burst.payload, burst.delta),
      onNext: (detail) => {
        this.addCommitting(key, -burst.delta);
        this.hooks.onCommitted(detail as ConsumableDetailResponse);
      },
      onError: (err) => {
        // Local truth for this key is suspect — drop this commit's delta, any taps accumulated
        // since, AND any queued (not yet running) commits for the same key.
        this.addCommitting(key, -burst.delta);
        this.discardKey(key);
        this.hooks.onError(burst.payload, err);
      },
    });
    this.pump();
  }

  flushAll(): void {
    for (const key of [...this._pending().keys()]) this.flush(key);
  }

  /** Serializes a non-stepper mutation (assign/move/correct/resync) through the same queue. */
  enqueue<T>(work: () => Observable<T>, onNext: (result: T) => void, onError: (err: unknown) => void): void {
    this.queue.push({
      run: work,
      onNext: (result) => onNext(result as T),
      onError,
    });
    this.pump();
  }

  /** Single-flight: runs queued jobs one at a time, in order. */
  private pump(): void {
    if (this.running) return;
    const job = this.queue.shift();
    if (!job) {
      this.settleIfIdle();
      return;
    }
    this.running = true;
    job.run().subscribe({
      next: (result) => {
        this.running = false;
        job.onNext(result);
        this.pump();
      },
      error: (err) => {
        this.running = false;
        job.onError(err);
        this.pump();
      },
    });
  }

  /** Drops everything outstanding for a failed key: pending taps, timer, and queued commits. */
  private discardKey(key: string): void {
    const cleaned = new Map(this._pending());
    cleaned.delete(key);
    this._pending.set(cleaned);
    this.cancelTimer(key);

    for (let i = this.queue.length - 1; i >= 0; i--) {
      const job = this.queue[i];
      if (job.stepperKey === key) {
        this.queue.splice(i, 1);
        this.addCommitting(key, -(job.stepperDelta ?? 0));
      }
    }
  }

  private addCommitting(key: string, delta: number): void {
    const next = new Map(this._committing());
    const updated = (next.get(key) ?? 0) + delta;
    if (updated === 0) next.delete(key);
    else next.set(key, updated);
    this._committing.set(next);
  }

  private hasWork(): boolean {
    return this._pending().size > 0 || this.queue.length > 0 || this.running;
  }

  private settleIfIdle(): void {
    if (this.hasWork()) return;
    const resolvers = this.idleResolvers;
    this.idleResolvers = [];
    for (const resolve of resolvers) resolve();
  }

  private cancelTimer(key: string): void {
    const timer = this.timers.get(key);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(key);
    }
  }
}
