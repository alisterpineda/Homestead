import { GenericContainer, Network, type StartedTestContainer } from 'testcontainers';
import path from 'node:path';
import { startPostgres } from './postgres.js';
import { startKeycloak } from './keycloak.js';
import { startHomestead, HOMESTEAD_CONFIGS } from './homestead.js';

const IMAGE_NAME = 'homestead:e2e';

export interface InfraResult {
  teardown: () => Promise<void>;
}

export async function startAll(): Promise<InfraResult> {
  const dockerfileDir = path.resolve(__dirname, '..', '..');

  // Build homestead:e2e image (uses Docker layer cache — fast if unchanged)
  // Set SKIP_BUILD=1 to reuse a pre-built image (e.g., CI pre-builds with GHA cache)
  if (!process.env.SKIP_BUILD) {
    console.log('Building homestead:e2e image...');
    await GenericContainer.fromDockerfile(dockerfileDir).build(IMAGE_NAME, {
      deleteOnExit: false,
    });
    console.log('Image built.');
  } else {
    console.log('Skipping image build (SKIP_BUILD is set).');
  }

  // Create shared Docker network
  const network = await new Network().start();

  // Start postgres + keycloak in parallel
  console.log('Starting postgres and keycloak...');
  const [postgres, keycloak] = await Promise.all([
    startPostgres(network),
    startKeycloak(network),
  ]);
  console.log('Postgres and keycloak are ready.');

  // Start all 3 homestead instances in parallel
  console.log('Starting homestead instances...');
  const homesteadContainers: StartedTestContainer[] = await Promise.all(
    HOMESTEAD_CONFIGS.map((config) => startHomestead(config, network, IMAGE_NAME)),
  );
  console.log('All homestead instances are ready.');

  const teardown = async () => {
    console.log('Stopping containers...');
    await Promise.all([...homesteadContainers, keycloak, postgres].map((c) => c.stop()));
    await network.stop();
    console.log('All containers stopped.');
  };

  return { teardown };
}
