import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@/shared/services/auth.service';

export const pendingActivationGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated() && !auth.isActive()) {
    return true;
  }

  return router.createUrlTree(['/']);
};
