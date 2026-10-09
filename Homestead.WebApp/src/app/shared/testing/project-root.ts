// Test-only module: excluded from the app build in tsconfig.app.json (uses node:fs).
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';

/**
 * The WebApp project root, for specs that read source files statically.
 *
 * The unit-test builder hands Vitest a bundle, so `import.meta.url` is not a `file:` URL. Anchor on
 * the nearest ancestor of the working directory that holds `angular.json` instead.
 */
export function projectRoot(): string {
  let dir = process.cwd();
  for (;;) {
    if (existsSync(join(dir, 'angular.json'))) return dir;
    const parent = dirname(dir);
    if (parent === dir) throw new Error(`Could not locate angular.json above ${process.cwd()}`);
    dir = parent;
  }
}
