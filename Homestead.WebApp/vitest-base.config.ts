import { defineConfig } from 'vitest/config';

/**
 * Loaded by `@angular/build:unit-test` (test target `runnerConfig: true`) and merged into the
 * builder's generated project config. The filename MUST be `vitest-base.config.*` — that is the only
 * pattern the builder's `findVitestBaseConfig` searches; a plain `vitest.config.ts` is silently
 * ignored (and `runnerConfig: true` then disables Vitest's own config search entirely). The builder
 * exposes no pool/worker option of its own (its schema is `additionalProperties: false`), so these
 * resource knobs can only be set here.
 *
 * maxWorkers — Vitest's `forks` pool (the v4 default) spawns one child process per spec file, sized
 * to the host CPU count. Each fork is a full V8 + jsdom + Angular framework (~0.5-1GB), so on a box
 * with many cores but limited RAM the pool can spike to several GB and, alongside another build or a
 * running dev server, push the machine into swap — at which point otherwise-instant tests trip
 * Vitest's default 5s timeout in scattered, unrelated files. Capping the pool bounds that spike (and
 * tends to run no slower, since the uncapped default just over-subscribes and contends). Tune to RAM,
 * not cores. Note v4 removed `poolOptions`; the cap is the top-level `maxWorkers` (or env
 * `VITEST_MAX_WORKERS`).
 *
 * testTimeout — a safety net for any residual stall under memory pressure. Generous because these are
 * jsdom unit tests; none legitimately runs for seconds, so a larger value masks no real hang.
 */
export default defineConfig({
  test: {
    maxWorkers: 4,
    testTimeout: 15_000,
  },
});
