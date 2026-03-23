import { HttpErrorResponse } from '@angular/common/http';

interface ApiProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
  errorCode?: string;
}

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

export function extractValidationErrors(err: unknown): Record<string, string[]> | null {
  if (err instanceof HttpErrorResponse && err.error) {
    const body = err.error as ApiProblemDetails;
    if (body.errors && Object.keys(body.errors).length > 0) return body.errors;
  }
  return null;
}

export function extractErrorCode(err: unknown): string | null {
  if (err instanceof HttpErrorResponse && err.error) {
    const body = err.error as ApiProblemDetails;
    return body.errorCode ?? null;
  }
  return null;
}
