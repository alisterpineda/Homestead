import {
  GenericContainer,
  type StartedNetwork,
  type StartedTestContainer,
  Wait,
} from 'testcontainers';
import path from 'node:path';
import { DB_PASSWORD } from './homestead.js';

export async function startPostgres(
  network: StartedNetwork,
): Promise<StartedTestContainer> {
  const initSql = path.resolve(__dirname, '..', 'init-databases.sql');

  return new GenericContainer('postgres:17-alpine')
    .withNetwork(network)
    .withNetworkAliases('postgres')
    .withEnvironment({
      POSTGRES_USER: 'postgres',
      POSTGRES_PASSWORD: DB_PASSWORD,
      POSTGRES_DB: 'homestead_local',
    })
    .withCopyFilesToContainer([
      { source: initSql, target: '/docker-entrypoint-initdb.d/init-databases.sql' },
    ])
    .withWaitStrategy(
      Wait.forHealthCheck()
    )
    .withHealthCheck({
      test: ['CMD-SHELL', 'pg_isready -U postgres'],
      interval: 5_000,
      timeout: 5_000,
      retries: 5,
    })
    .start();
}
