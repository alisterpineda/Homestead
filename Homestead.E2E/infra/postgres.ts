import {
  GenericContainer,
  type StartedNetwork,
  type StartedTestContainer,
  Wait,
} from 'testcontainers';

export const DB_NAME = 'homestead_e2e';
export const DB_PASSWORD = 'e2e-test-password';

export async function startPostgres(network: StartedNetwork): Promise<StartedTestContainer> {
  return new GenericContainer('postgres:17-alpine')
    .withNetwork(network)
    .withNetworkAliases('postgres')
    .withEnvironment({
      POSTGRES_USER: 'postgres',
      POSTGRES_PASSWORD: DB_PASSWORD,
      POSTGRES_DB: DB_NAME,
    })
    .withHealthCheck({
      test: ['CMD-SHELL', 'pg_isready -U postgres'],
      interval: 5_000,
      timeout: 5_000,
      retries: 5,
    })
    .withWaitStrategy(Wait.forHealthCheck())
    .start();
}
