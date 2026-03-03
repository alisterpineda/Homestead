import {
  GenericContainer,
  type StartedNetwork,
  type StartedTestContainer,
  Wait,
} from 'testcontainers';
import path from 'node:path';

export const KEYCLOAK_HOST_PORT = 8888;

export async function startKeycloak(
  network: StartedNetwork,
): Promise<StartedTestContainer> {
  const realmDir = path.resolve(__dirname, '..', '..', 'Homestead.AppHost', 'KeycloakRealms');

  return new GenericContainer('quay.io/keycloak/keycloak:26.2')
    .withNetwork(network)
    .withNetworkAliases('keycloak')
    .withCommand(['start-dev', '--import-realm'])
    .withExposedPorts({ container: 8080, host: KEYCLOAK_HOST_PORT }, 9000)
    .withEnvironment({
      KC_BOOTSTRAP_ADMIN_USERNAME: 'admin',
      KC_BOOTSTRAP_ADMIN_PASSWORD: 'admin',
      KC_HEALTH_ENABLED: 'true',
      KC_HOSTNAME: `http://localhost:${KEYCLOAK_HOST_PORT}`,
      KC_HOSTNAME_BACKCHANNEL_DYNAMIC: 'true',
    })
    .withBindMounts([
      { source: realmDir, target: '/opt/keycloak/data/import', mode: 'ro' },
    ])
    .withWaitStrategy(
      Wait.forHttp('/health/ready', 9000).forStatusCode(200),
    )
    .withStartupTimeout(120_000)
    .start();
}
