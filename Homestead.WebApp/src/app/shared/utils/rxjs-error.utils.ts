import { type WritableSignal } from '@angular/core';
import { type Observable, catchError, EMPTY, of } from 'rxjs';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

/**
 * Catches mutation errors: resets loading signal, shows toast for client errors
 * (server/network errors are already toasted by the error interceptor), and
 * completes the stream.
 *
 * For handlers needing custom error logic (status code checks, inline error
 * display), use `ErrorHandlerService` directly in `.subscribe({ error })`.
 */
export function handleMutationError<T>(
  errorHandler: ErrorHandlerService,
  loadingSignal: WritableSignal<boolean>,
  fallbackMessage: string,
): (source: Observable<T>) => Observable<T> {
  return catchError<T, Observable<never>>((err: unknown) => {
    loadingSignal.set(false);
    if (!errorHandler.isServerOrNetworkError(err)) {
      errorHandler.handleError(err, fallbackMessage);
    }
    return EMPTY;
  });
}

/**
 * Catches query errors in data-loading pipelines: resets loading signal,
 * sets error flag, and returns a fallback value to keep the stream alive.
 *
 * No toast is shown — data-loading failures render an error state in the UI.
 */
export function handleQueryError<T, F>(
  loadingSignal: WritableSignal<boolean>,
  errorSignal: WritableSignal<boolean>,
  fallbackValue: F,
): (source: Observable<T>) => Observable<T | F> {
  return catchError<T, Observable<F>>(() => {
    loadingSignal.set(false);
    errorSignal.set(true);
    return of(fallbackValue);
  });
}
