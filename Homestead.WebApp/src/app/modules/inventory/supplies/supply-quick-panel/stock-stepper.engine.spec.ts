import { Subject, of, throwError } from 'rxjs';
import { StockStepperEngine, stockLocationKey, type StepperHooks } from './stock-stepper.engine';
import type { SupplyDetailResponse } from '@/api/generated/models/supply-detail-response';

const DETAIL = {
  supply: { id: 's1', name: 'Paper towels' },
  locations: [],
} as unknown as SupplyDetailResponse;

const KEY_A = 'area-1|';
const KEY_B = '|container-1';

interface CommitCall {
  payload: string;
  delta: number;
  response: Subject<SupplyDetailResponse>;
}

/** Hooks whose commits stay open until the test resolves them — for serialization tests. */
function makeControlledHooks() {
  const commits: CommitCall[] = [];
  const committed: SupplyDetailResponse[] = [];
  const errors: { payload: string; err: unknown }[] = [];
  const hooks: StepperHooks<string> = {
    commit: (payload, delta) => {
      const response = new Subject<SupplyDetailResponse>();
      commits.push({ payload, delta, response });
      return response.asObservable();
    },
    onCommitted: (detail) => committed.push(detail),
    onError: (payload, err) => errors.push({ payload, err }),
  };
  return { hooks, commits, committed, errors };
}

describe('stockLocationKey', () => {
  it('keys on the area/container pair, not the entry id', () => {
    expect(stockLocationKey({ areaId: 'a1', containerItemId: null })).toBe('a1|');
    expect(stockLocationKey({ areaId: null, containerItemId: 'c1' })).toBe('|c1');
    expect(stockLocationKey({})).toBe('|');
  });
});

describe('StockStepperEngine', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('debounced taps', () => {
    it('commits the net delta of a burst as one call after the debounce window', () => {
      const { hooks, commits, committed } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_A, 1, KEY_A);
      expect(commits.length).toBe(0);
      expect(engine.deltaFor(KEY_A)).toBe(3);

      vi.advanceTimersByTime(800);
      expect(commits.length).toBe(1);
      expect(commits[0]).toMatchObject({ payload: KEY_A, delta: 3 });

      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(committed).toEqual([DETAIL]);
      expect(engine.deltaFor(KEY_A)).toBe(0);
      expect(engine.isBusy()).toBe(false);
    });

    it('restarts the debounce timer on every tap', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(700);
      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(700);
      expect(commits.length).toBe(0);

      vi.advanceTimersByTime(100);
      expect(commits.length).toBe(1);
      expect(commits[0].delta).toBe(2);
    });

    it('mixes − and + into a single net delta', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_A, -1, KEY_A);
      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(800);

      expect(commits.length).toBe(1);
      expect(commits[0].delta).toBe(2);
    });

    it('drops a net-zero burst without any API call', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_A, -1, KEY_A);
      expect(engine.deltaFor(KEY_A)).toBe(0);
      expect(engine.isBusy()).toBe(false);

      vi.advanceTimersByTime(1000);
      expect(commits.length).toBe(0);
    });

    it('debounces each location key independently', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(400);
      engine.tap(KEY_B, -1, KEY_B);
      expect(engine.totalDelta()).toBe(0); // +1 and −1 across keys

      vi.advanceTimersByTime(400);
      expect(commits.length).toBe(1);
      expect(commits[0]).toMatchObject({ payload: KEY_A, delta: 1 });
      commits[0].response.next(DETAIL);
      commits[0].response.complete();

      vi.advanceTimersByTime(400);
      expect(commits.length).toBe(2);
      expect(commits[1]).toMatchObject({ payload: KEY_B, delta: -1 });
    });

    it('uses the payload captured at tap time for the commit', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, 'payload-at-tap-time');
      vi.advanceTimersByTime(800);

      expect(commits[0].payload).toBe('payload-at-tap-time');
    });
  });

  describe('single-flight serialization', () => {
    it('runs one commit at a time, in FIFO order', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_B, 1, KEY_B);
      vi.advanceTimersByTime(800);

      // Both bursts flushed, but only the first commit is in flight.
      expect(commits.length).toBe(1);
      expect(commits[0].payload).toBe(KEY_A);

      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(commits.length).toBe(2);
      expect(commits[1].payload).toBe(KEY_B);
    });

    it('keeps a queued (not yet running) commit in the optimistic delta', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_B, 1, KEY_B);
      vi.advanceTimersByTime(800);

      // A is in flight, B is queued behind it — BOTH must stay visible to the display.
      expect(commits.length).toBe(1);
      expect(engine.deltaFor(KEY_A)).toBe(1);
      expect(engine.deltaFor(KEY_B)).toBe(1);
      expect(engine.totalDelta()).toBe(2);

      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(engine.deltaFor(KEY_A)).toBe(0);
      expect(engine.deltaFor(KEY_B)).toBe(1); // now in flight

      commits[1].response.next(DETAIL);
      commits[1].response.complete();
      expect(engine.totalDelta()).toBe(0);
    });

    it('accumulates taps during a flight as a fresh burst committed afterwards', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(800);
      expect(commits.length).toBe(1);

      // In-flight + a new burst on the same key: optimistic delta shows both.
      engine.tap(KEY_A, 1, KEY_A);
      expect(engine.deltaFor(KEY_A)).toBe(2);
      expect(engine.totalDelta()).toBe(2);

      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(engine.deltaFor(KEY_A)).toBe(1); // in-flight cleared, fresh burst remains

      vi.advanceTimersByTime(800);
      expect(commits.length).toBe(2);
      expect(commits[1].delta).toBe(1);
    });

    it('serializes enqueued mutations with stepper commits', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(800);

      const work = new Subject<string>();
      let workSubscribed = false;
      let workResult: string | undefined;
      engine.enqueue(
        () => {
          workSubscribed = true;
          return work.asObservable();
        },
        (r) => (workResult = r),
        () => {},
      );

      // Queued behind the in-flight stepper commit.
      expect(workSubscribed).toBe(false);
      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(workSubscribed).toBe(true);

      work.next('done');
      work.complete();
      expect(workResult).toBe('done');
      expect(engine.isBusy()).toBe(false);
    });
  });

  describe('error handling', () => {
    it('drops in-flight and pending state for the failed key, reports it, and pumps the next job', () => {
      const { hooks, commits, errors, committed } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_B, 1, KEY_B);
      vi.advanceTimersByTime(800);

      // Taps on KEY_A while its commit is in flight — these must be discarded on failure too.
      engine.tap(KEY_A, 1, KEY_A);
      expect(engine.deltaFor(KEY_A)).toBe(2);

      const boom = new Error('409 stock changed');
      commits[0].response.error(boom);

      expect(errors).toEqual([{ payload: KEY_A, err: boom }]);
      expect(engine.deltaFor(KEY_A)).toBe(0);

      // The queue moves on to KEY_B's commit — a different key is unaffected.
      expect(commits.length).toBe(2);
      expect(commits[1].payload).toBe(KEY_B);
      commits[1].response.next(DETAIL);
      commits[1].response.complete();
      expect(committed).toEqual([DETAIL]);
      expect(engine.isBusy()).toBe(false);
    });

    it('purges queued (not yet running) commits for the failed key', () => {
      const { hooks, commits, errors } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      // Commit 1 for A goes in flight; a second A burst and a B burst get queued behind it.
      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(800);
      engine.tap(KEY_A, 1, KEY_A);
      vi.advanceTimersByTime(800);
      engine.tap(KEY_B, 1, KEY_B);
      vi.advanceTimersByTime(800);
      expect(commits.length).toBe(1);
      expect(engine.deltaFor(KEY_A)).toBe(2); // in flight + queued

      const boom = new Error('409 stock changed');
      commits[0].response.error(boom);

      // A's queued commit is discarded (local truth suspect); B's commit proceeds.
      expect(errors.length).toBe(1);
      expect(engine.deltaFor(KEY_A)).toBe(0);
      expect(commits.length).toBe(2);
      expect(commits[1].payload).toBe(KEY_B);
      commits[1].response.next(DETAIL);
      commits[1].response.complete();
      expect(engine.isBusy()).toBe(false);
    });

    it('routes enqueued-job errors to the job handler, not the stepper hook', () => {
      const { hooks, errors } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      const boom = new Error('400 no-op');
      let jobErr: unknown;
      engine.enqueue(
        () => throwError(() => boom),
        () => {},
        (err) => (jobErr = err),
      );

      expect(jobErr).toBe(boom);
      expect(errors.length).toBe(0);
    });
  });

  describe('flush and whenIdle', () => {
    it('flushAll commits every pending burst immediately', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 2, KEY_A);
      engine.tap(KEY_B, -1, KEY_B);
      engine.flushAll();

      expect(commits.length).toBe(1); // single-flight: A in flight, B queued
      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      expect(commits.length).toBe(2);
      expect(commits.map((c) => c.payload)).toEqual([KEY_A, KEY_B]);

      // The original timers must not fire a second commit.
      commits[1].response.next(DETAIL);
      commits[1].response.complete();
      vi.advanceTimersByTime(2000);
      expect(commits.length).toBe(2);
    });

    it('flush of a key with no pending burst is a no-op', () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);
      engine.flush(KEY_A);
      expect(commits.length).toBe(0);
    });

    it('whenIdle resolves immediately when there is no work', async () => {
      const { hooks } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);
      await expect(engine.whenIdle()).resolves.toBeUndefined();
    });

    it('whenIdle waits for pending, queued, and in-flight work to settle', async () => {
      const { hooks, commits } = makeControlledHooks();
      const engine = new StockStepperEngine(hooks);

      engine.tap(KEY_A, 1, KEY_A);
      engine.tap(KEY_B, 1, KEY_B);
      engine.flushAll();

      let idle = false;
      const promise = engine.whenIdle().then(() => (idle = true));

      commits[0].response.next(DETAIL);
      commits[0].response.complete();
      await Promise.resolve();
      expect(idle).toBe(false); // B still in flight

      commits[1].response.next(DETAIL);
      commits[1].response.complete();
      await promise;
      expect(idle).toBe(true);
    });

    it('completes a flush synchronously when the commit observable is synchronous', () => {
      const committed: SupplyDetailResponse[] = [];
      const engine = new StockStepperEngine<string>({
        commit: () => of(DETAIL),
        onCommitted: (d) => committed.push(d),
        onError: () => {},
      });

      engine.tap(KEY_A, 1, KEY_A);
      engine.flush(KEY_A);
      expect(committed).toEqual([DETAIL]);
      expect(engine.isBusy()).toBe(false);
    });
  });
});
