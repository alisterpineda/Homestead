import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';

export const setupGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.needsSetup()) {
    return true;
  }

  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/documents']);
  }

  return router.createUrlTree(['/login']);
};
