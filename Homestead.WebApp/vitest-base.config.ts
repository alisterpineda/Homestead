import { defineConfig } from 'vitest/config';

/**
 * Loaded by `@angular/build:unit-test` (test target `runnerConfig: true`) and merged into the
 * builder's generated project config. The filename MUST be `vitest-base.config.*` — that is the only
 * pattern the builder's `findVitestBaseConfig` searches; a plain `vitest.config.ts` is silently
 * ignored (and `runnerConfig: true` then disables Vitest's own config search entirely).
 *
 * Keep it to a couple of resource knobs only — the builder owns the worker pool, isolation,
 * environment, and plugins, so `maxWorkers`/`pool` settings here are inert; `testTimeout` is a
 * per-project option and does merge through (verified: a 6s test passes only with this in place).
 *
 * Why testTimeout: the unit-test builder bundles every spec with esbuild (a memory-heavy phase) and
 * runs them in parallel. On a host under memory pressure — e.g. another build or a dev server
 * running alongside — the machine can swap, and otherwise-instant unit tests trip Vitest's default
 * 5s timeout in scattered, unrelated files. A larger timeout absorbs that transient stall without
 * masking a genuine hang (these are jsdom unit tests; none legitimately runs for seconds).
 */
export default defineConfig({
  test: {
    testTimeout: 15_000,
  },
});
