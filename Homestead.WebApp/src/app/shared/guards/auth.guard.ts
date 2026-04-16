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

  return router.createUrlTree(['/login']);
};
