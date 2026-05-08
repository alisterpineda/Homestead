import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { AuthService } from '@/shared/services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  const errorHandler = inject(ErrorHandlerService);

  return next(req).pipe(
    tap({
      error: (err) => {
        if (err.status === 403 && errorHandler.getErrorCode(err) === 'account_inactive') {
          router.navigate(['/pending-activation']);
          return;
        }
        if (err.status === 401 && !req.url.includes('/api/auth/')) {
          auth.clearUser();
          router.navigate(['/login']);
        }
      },
    }),
  );
};
