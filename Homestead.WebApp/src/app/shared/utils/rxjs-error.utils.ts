import { type WritableSignal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
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

/**
 * Detail-page load-error handler. A real 404 → notFound (terminal "not found" state). Any other
 * error → a retryable loadError flag and EMPTY, so an already-rendered page is NEVER replaced by an
 * error state on a failed refetch. Reset loadError (and notFound) at the top of the switchMap before
 * each fetch, exactly like the existing notFound reset.
 *
 * Use inside the inner (per-fetch) observable's `catchError`, e.g.
 *   switchMap(() => api.get(id).pipe(catchError((err) => handleDetailLoadError(err, { notFound, loadError }, loading))))
 * On a non-404 the returned EMPTY emits nothing, so `toSignal` keeps its previous (rendered) value.
 */
export function handleDetailLoadError(
  err: unknown,
  flags: { notFound: WritableSignal<boolean>; loadError: WritableSignal<boolean> },
  loading: WritableSignal<boolean>,
): Observable<null> {
  loading.set(false);
  if (err instanceof HttpErrorResponse && err.status === 404) {
    flags.notFound.set(true);
    return of(null); // blank → not-found state (there is genuinely nothing to show)
  }
  flags.loadError.set(true);
  return EMPTY as unknown as Observable<null>; // keep the last-emitted (rendered) value in toSignal
}
