import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';
import { isSafeReturnPath } from '@/shared/utils/safe-return-path';

export const loginGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return true;
  }

  if (!auth.isActive()) {
    return router.createUrlTree(['/pending-activation']);
  }

  const returnUrl = route.queryParamMap.get('returnUrl');
  const target = isSafeReturnPath(returnUrl) ? returnUrl : '/documents';
  return router.parseUrl(target);
};
