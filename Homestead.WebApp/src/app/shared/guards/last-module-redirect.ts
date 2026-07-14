import { inject } from '@angular/core';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';

/**
 * Functional route redirect for the empty path ('/'): sends the user to the module they were
 * last in (restored on PWA cold start), or the default when none is recorded. Runs in an
 * injection context (Angular Router RedirectFunction). Role gating for /admin is handled by the
 * child route's adminGuard, so this stays deliberately role-agnostic.
 */
export function lastModuleRedirect(): string {
  return inject(UserPreferencesService).lastModulePath();
}
