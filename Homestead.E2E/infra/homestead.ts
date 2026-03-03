import {
  GenericContainer,
  type StartedNetwork,
  type StartedTestContainer,
  Wait,
} from 'testcontainers';
import { KEYCLOAK_HOST_PORT } from './keycloak.js';

export const DB_PASSWORD = 'e2e-test-password';

interface BaseConfig {
  name: string;
  hostPort: number;
  dbName: string;
}

interface LocalOnlyConfig extends BaseConfig {
  oidc: false;
}

interface OidcConfig extends BaseConfig {
  oidc: true;
  disableLocalLogin: boolean;
}

export type HomesteadConfig = LocalOnlyConfig | OidcConfig;

const COMMON_OIDC = {
  OIDC_AUTHORITY: `http://localhost:${KEYCLOAK_HOST_PORT}/realms/homestead`,
  OIDC_METADATA_ADDRESS:
    'http://keycloak:8080/realms/homestead/.well-known/openid-configuration',
  OIDC_CLIENT_ID: 'homestead-app',
  OIDC_CLIENT_SECRET: 'homestead-dev-secret',
  OIDC_DISPLAY_NAME: 'Keycloak',
  OIDC_ROLE_CLAIM: 'groups',
  OIDC_ADMIN_CLAIM_VALUE: 'homestead-admin',
};

export const HOMESTEAD_CONFIGS: HomesteadConfig[] = [
  {
    name: 'homestead-local',
    hostPort: 3101,
    dbName: 'homestead_local',
    oidc: false,
  },
  {
    name: 'homestead-oidc-local',
    hostPort: 3102,
    dbName: 'homestead_oidc_local',
    oidc: true,
    disableLocalLogin: false,
  },
  {
    name: 'homestead-oidc-only',
    hostPort: 3103,
    dbName: 'homestead_oidc_only',
    oidc: true,
    disableLocalLogin: true,
  },
];

export async function startHomestead(
  config: HomesteadConfig,
  network: StartedNetwork,
  imageName: string,
): Promise<StartedTestContainer> {
  const env: Record<string, string> = {
    DB_HOST: 'postgres',
    DB_NAME: config.dbName,
    DB_PASSWORD,
  };

  if (config.oidc) {
    Object.assign(env, COMMON_OIDC);
    env.ASPNETCORE_ENVIRONMENT = 'Development';
    if (config.disableLocalLogin) {
      env.OIDC_DISABLE_LOCAL_LOGIN = 'true';
    }
  }

  return new GenericContainer(imageName)
    .withNetwork(network)
    .withNetworkAliases(config.name)
    .withExposedPorts({ container: 8080, host: config.hostPort })
    .withEnvironment(env)
    .withWaitStrategy(
      Wait.forHttp('/health', 8080).forStatusCode(200),
    )
    .withStartupTimeout(60_000)
    .start();
}
