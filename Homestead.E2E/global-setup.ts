import { rm } from 'node:fs/promises';
import { AUTH_DIR } from './e2e.env';
import { startAll } from './infra/start-all';

export default async function globalSetup(): Promise<() => Promise<void>> {
  const { teardown } = await startAll();
  return async () => {
    await teardown();
    await rm(AUTH_DIR, { recursive: true, force: true });
  };
}
