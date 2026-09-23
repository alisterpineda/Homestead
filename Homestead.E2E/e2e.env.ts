import { execFileSync } from 'node:child_process';

/**
 * Host ports for the app and Keycloak containers.
 *
 * Picked once, by the runner process, from ports the OS reports free, so two runs on one host
 * never collide. The runner loads the config (and so this module) before it forks workers, and
 * workers inherit its environment — they read the same ports back instead of picking new ones.
 * Set E2E_APP_PORT / E2E_KEYCLOAK_PORT to pin them (e.g. to reach a run from outside).
 */
function freePorts(count: number): number[] {
  // Config loading is synchronous, so ask a child Node process to bind port 0 and report back.
  const script = `
    const net = require('node:net');
    const servers = Array.from({ length: ${count} }, () => net.createServer());
    Promise.all(servers.map((s) => new Promise((r) => s.listen(0, '127.0.0.1', r)))).then(() => {
      console.log(servers.map((s) => s.address().port).join(','));
      servers.forEach((s) => s.close());
    });`;
  return execFileSync(process.execPath, ['-e', script], { encoding: 'utf8' }).trim().split(',').map(Number);
}

if (!process.env.E2E_APP_PORT || !process.env.E2E_KEYCLOAK_PORT) {
  const [app, keycloak] = freePorts(2);
  process.env.E2E_APP_PORT ??= String(app);
  process.env.E2E_KEYCLOAK_PORT ??= String(keycloak);
}

export const APP_PORT = Number(process.env.E2E_APP_PORT);
export const KEYCLOAK_PORT = Number(process.env.E2E_KEYCLOAK_PORT);

export const BASE_URL = `http://localhost:${APP_PORT}`;
export const KEYCLOAK_URL = `http://localhost:${KEYCLOAK_PORT}`;

/**
 * Where this run keeps its signed-in storage state. Per run, because a session cookie is only
 * valid against the stack that issued it — concurrent runs must not overwrite each other's.
 */
export const AUTH_DIR = `.auth/run-${APP_PORT}`;

/** Docker image the app container runs; built by global setup unless SKIP_BUILD is set. */
export const APP_IMAGE = process.env.E2E_IMAGE ?? 'homestead:e2e';
