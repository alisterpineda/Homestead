import { inject } from '@angular/core';
import { CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { isSafeReturnPath } from '@/shared/utils/safe-return-path';

export const authGuard: CanActivateFn = (_route, state: RouterStateSnapshot) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    if (!auth.isActive()) {
      return router.createUrlTree(['/pending-activation']);
    }
    return true;
  }

  const queryParams =
    isSafeReturnPath(state.url) && state.url !== '/' ? { returnUrl: state.url } : undefined;
  return router.createUrlTree(['/login'], queryParams ? { queryParams } : undefined);
};
