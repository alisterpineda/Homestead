import { startAll } from './infra/start-all.js';

async function globalSetup(): Promise<() => Promise<void>> {
  const { teardown } = await startAll();
  return teardown;
}

export default globalSetup;
