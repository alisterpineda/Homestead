import { GenericContainer, Network } from 'testcontainers';
import path from 'node:path';
import { APP_IMAGE, BASE_URL } from '../e2e.env';
import { startHomestead } from './homestead';
import { startKeycloak } from './keycloak';
import { startPostgres } from './postgres';

export async function startAll(): Promise<{ teardown: () => Promise<void> }> {
  // Uses the Docker layer cache, so it is fast when nothing changed. Set SKIP_BUILD=1 to reuse a
  // pre-built image instead (CI pulls the one its build job pushed).
  if (!process.env.SKIP_BUILD) {
    console.log(`Building ${APP_IMAGE}...`);
    await GenericContainer.fromDockerfile(path.resolve(__dirname, '..', '..')).build(APP_IMAGE, {
      deleteOnExit: false,
    });
  } else {
    console.log(`Skipping image build (SKIP_BUILD is set); using ${APP_IMAGE}.`);
  }

  const network = await new Network().start();

  console.log('Starting postgres and keycloak...');
  const [postgres, keycloak] = await Promise.all([startPostgres(network), startKeycloak(network)]);

  console.log('Starting homestead...');
  const homestead = await startHomestead(network);
  console.log(`Homestead is ready at ${BASE_URL}.`);

  return {
    teardown: async () => {
      await Promise.all([homestead, keycloak, postgres].map((c) => c.stop()));
      await network.stop();
    },
  };
}
