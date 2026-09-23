import { escapeRegExp } from './helpers/playwright.helpers';
import { AUTH_DIR, KEYCLOAK_URL } from './e2e.env';

/** Users pre-imported into the Keycloak realm (Homestead.AppHost/KeycloakRealms). */
export const KEYCLOAK_ADMIN = { email: 'admin@test.com', password: 'admin123' };
export const KEYCLOAK_USER = { email: 'user@test.com', password: 'user123' };

export const KEYCLOAK_URL_PATTERN = new RegExp(`^${escapeRegExp(KEYCLOAK_URL)}/`);

/** Signed-in browser state written by tests/setup/auth.setup.ts. */
export const AUTH_STATE = {
  admin: `${AUTH_DIR}/admin.json`,
  contributor: `${AUTH_DIR}/contributor.json`,
};

/** Household-wide finance display currency, set once by the setup project. */
export const DISPLAY_CURRENCY = 'CAD';

/**
 * Names for `test.describe(..., { lock })`. Tests holding the same lock never run concurrently —
 * across files, workers and device projects — while everything else runs in parallel. Take one
 * only for state that genuinely can't be partitioned by unique names.
 */
export const LOCKS = {
  /** The trash is one household-wide bin: "Empty trash" purges every test's trashed documents. */
  documentsTrash: 'documents-trash',
  /** Household-wide Documents settings (e.g. the default text-extraction engine). */
  documentsSettings: 'documents-settings',
  /** A user may have only one ZIP download in flight, and every spec runs as the same admin. */
  documentsDownload: 'documents-download',
  /** Household-wide Finances settings (display currency, price sources). */
  financesSettings: 'finances-settings',
  /** Net worth sums every account: a spec asserting a total owns the far-past window it writes to. */
  financesNetWorth: 'finances-net-worth',
} as const;
