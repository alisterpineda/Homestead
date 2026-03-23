import { HttpErrorResponse } from '@angular/common/http';
import type { ApiProblemDetails } from '@/shared/types/api-problem-details';

/** @deprecated Use `ErrorHandlerService.getErrorMessage()` or `ErrorHandlerService.handleError()` instead. */
export function extractApiError(err: unknown, fallback: string): string {
  if (err instanceof HttpErrorResponse && err.error) {
    const body = err.error as ApiProblemDetails;
    if (body.detail) return body.detail;
    if (body.errors) {
      const firstField = Object.values(body.errors)[0];
      if (firstField?.[0]) return firstField[0];
    }
  }
  return fallback;
}

/** @deprecated Use `ErrorHandlerService.getValidationErrors()` instead. */
export function extractValidationErrors(err: unknown): Record<string, string[]> | null {
  if (err instanceof HttpErrorResponse && err.error) {
    const body = err.error as ApiProblemDetails;
    if (body.errors && Object.keys(body.errors).length > 0) return body.errors;
  }
  return null;
}

/** @deprecated Use `ErrorHandlerService.getErrorCode()` instead. */
export function extractErrorCode(err: unknown): string | null {
  if (err instanceof HttpErrorResponse && err.error) {
    const body = err.error as ApiProblemDetails;
    return body.errorCode ?? null;
  }
  return null;
}
