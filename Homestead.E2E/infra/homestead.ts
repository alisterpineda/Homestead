import {
  GenericContainer,
  type StartedNetwork,
  type StartedTestContainer,
  Wait,
} from 'testcontainers';
import { APP_IMAGE, APP_PORT, KEYCLOAK_URL } from '../e2e.env';
import { DB_NAME, DB_PASSWORD } from './postgres';

export async function startHomestead(network: StartedNetwork): Promise<StartedTestContainer> {
  return new GenericContainer(APP_IMAGE)
    .withNetwork(network)
    .withNetworkAliases('homestead')
    .withExposedPorts({ container: 8080, host: APP_PORT })
    .withEnvironment({
      DB_HOST: 'postgres',
      DB_NAME,
      DB_PASSWORD,
      ASPNETCORE_ENVIRONMENT: 'Development',
      OIDC_AUTHORITY: `${KEYCLOAK_URL}/realms/homestead`,
      OIDC_METADATA_ADDRESS: 'http://keycloak:8080/realms/homestead/.well-known/openid-configuration',
      OIDC_CLIENT_ID: 'homestead-app',
      OIDC_CLIENT_SECRET: 'homestead-dev-secret',
      OIDC_DISPLAY_NAME: 'Keycloak',
      OIDC_ROLE_CLAIM: 'groups',
      OIDC_ADMIN_CLAIM_VALUES: 'homestead-admin',
    })
    .withWaitStrategy(Wait.forHttp('/health', 8080).forStatusCode(200))
    .withStartupTimeout(60_000)
    .start();
}
