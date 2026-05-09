import { startAll } from './infra/start-all.js';
import { ensureFixtureFiles } from './helpers/generate-fixtures';

async function globalSetup(): Promise<() => Promise<void>> {
  await ensureFixtureFiles();
  const { teardown } = await startAll();
  return teardown;
}

export default globalSetup;
