import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const errorHandler = inject(ErrorHandlerService);

  return next(req).pipe(
    tap({
      error: (err) => {
        if (errorHandler.isServerOrNetworkError(err)) {
          errorHandler.handleError(err, 'An unexpected error occurred.');
        }
      },
    }),
  );
};
