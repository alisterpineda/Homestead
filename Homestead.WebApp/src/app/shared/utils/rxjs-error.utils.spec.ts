import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { toast } from 'ngx-sonner';
import { handleMutationError, handleQueryError } from './rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

describe('rxjs-error.utils', () => {
  let errorHandler: ErrorHandlerService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    errorHandler = TestBed.inject(ErrorHandlerService);
    vi.spyOn(toast, 'error').mockImplementation(() => undefined as never);
    vi.spyOn(errorHandler, 'handleError');
    vi.spyOn(errorHandler, 'isServerOrNetworkError').mockImplementation(
      (err) => err instanceof HttpErrorResponse && (err.status === 0 || err.status >= 500),
    );
  });

  function httpError(status: number, body?: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error: body, url: '/api/test' });
  }

  describe('handleMutationError', () => {
    it('should reset loading signal on error', () => {
      const loading = signal(true);
      const err = httpError(400, { detail: 'Bad input' });

      throwError(() => err).pipe(
        handleMutationError(errorHandler, loading, 'Failed.'),
      ).subscribe();

      expect(loading()).toBe(false);
    });

    it('should show toast for client errors (4xx)', () => {
      const loading = signal(true);
      const err = httpError(400, { detail: 'Invalid name' });

      throwError(() => err).pipe(
        handleMutationError(errorHandler, loading, 'Failed.'),
      ).subscribe();

      expect(errorHandler.handleError).toHaveBeenCalledWith(err, 'Failed.');
    });

    it('should NOT show toast for server errors (5xx) — interceptor handles those', () => {
      const loading = signal(true);
      const err = httpError(500, { detail: 'Server Error' });

      throwError(() => err).pipe(
        handleMutationError(errorHandler, loading, 'Failed.'),
      ).subscribe();

      expect(errorHandler.handleError).not.toHaveBeenCalled();
    });

    it('should complete the stream without emitting', () => {
      const loading = signal(true);
      const nextFn = vi.fn();
      const errorFn = vi.fn();
      const completeFn = vi.fn();

      throwError(() => httpError(400)).pipe(
        handleMutationError(errorHandler, loading, 'Failed.'),
      ).subscribe({ next: nextFn, error: errorFn, complete: completeFn });

      expect(nextFn).not.toHaveBeenCalled();
      expect(errorFn).not.toHaveBeenCalled();
      expect(completeFn).toHaveBeenCalled();
    });

    it('should pass through successful emissions', () => {
      const loading = signal(true);
      const nextFn = vi.fn();

      of('success').pipe(
        handleMutationError(errorHandler, loading, 'Failed.'),
      ).subscribe({ next: nextFn });

      expect(nextFn).toHaveBeenCalledWith('success');
      expect(errorHandler.handleError).not.toHaveBeenCalled();
    });
  });

  describe('handleQueryError', () => {
    it('should reset loading signal and set error flag', () => {
      const loading = signal(true);
      const error = signal(false);

      throwError(() => httpError(500)).pipe(
        handleQueryError(loading, error, null),
      ).subscribe();

      expect(loading()).toBe(false);
      expect(error()).toBe(true);
    });

    it('should emit the fallback value', () => {
      const loading = signal(true);
      const error = signal(false);
      const nextFn = vi.fn();

      throwError(() => httpError(500)).pipe(
        handleQueryError(loading, error, null),
      ).subscribe({ next: nextFn });

      expect(nextFn).toHaveBeenCalledWith(null);
    });

    it('should work with non-null fallback values', () => {
      const loading = signal(true);
      const error = signal(false);
      const nextFn = vi.fn();

      throwError(() => httpError(500)).pipe(
        handleQueryError(loading, error, []),
      ).subscribe({ next: nextFn });

      expect(nextFn).toHaveBeenCalledWith([]);
    });

    it('should pass through successful emissions', () => {
      const loading = signal(true);
      const error = signal(false);
      const nextFn = vi.fn();

      of({ items: [] }).pipe(
        handleQueryError(loading, error, null),
      ).subscribe({ next: nextFn });

      expect(nextFn).toHaveBeenCalledWith({ items: [] });
      expect(error()).toBe(false);
    });
  });
});
