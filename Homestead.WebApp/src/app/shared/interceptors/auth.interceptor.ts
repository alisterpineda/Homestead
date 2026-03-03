import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);

  return next(req).pipe(
    tap({
      error: (err) => {
        if (err.status === 403 && err.error?.error === 'account_inactive') {
          router.navigate(['/pending-activation']);
          return;
        }
        if (err.status === 401 && !req.url.includes('/api/auth/')) {
          router.navigate(['/login']);
        }
      },
    }),
  );
};
