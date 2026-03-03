import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    if (!auth.isActive()) {
      return router.createUrlTree(['/pending-activation']);
    }
    return true;
  }

  if (auth.needsSetup() && !auth.oidcEnabled()) {
    return router.createUrlTree(['/setup']);
  }

  return router.createUrlTree(['/login']);
};
