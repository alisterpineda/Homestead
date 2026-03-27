import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { toast } from '@spartan-ng/brain/sonner';
import type { ApiProblemDetails } from '@/shared/types/api-problem-details';

const STATUS_MESSAGES: Record<number, string> = {
  0: 'Unable to reach the server. Check your connection and try again.',
  400: 'The request was invalid.',
  403: 'You do not have permission to perform this action.',
  404: 'The requested resource was not found.',
  408: 'The request timed out. Please try again.',
  409: 'A conflict occurred. The resource may have been modified.',
  422: 'The submitted data is invalid.',
  429: 'Too many requests. Please wait a moment and try again.',
  500: 'An unexpected server error occurred. Please try again later.',
  502: 'The server is temporarily unavailable. Please try again later.',
  503: 'The service is temporarily unavailable. Please try again later.',
  504: 'The server took too long to respond. Please try again later.',
};

@Injectable({ providedIn: 'root' })
export class ErrorHandlerService {
  getErrorMessage(err: unknown, fallback: string): string {
    if (!(err instanceof HttpErrorResponse)) return fallback;

    if (err.status === 0) return STATUS_MESSAGES[0];

    const body = err.error as ApiProblemDetails | undefined;
    if (body) {
      if (body.detail) return body.detail;
      if (body.errors) {
        const firstField = Object.values(body.errors)[0];
        if (firstField?.[0]) return firstField[0];
      }
    }

    return STATUS_MESSAGES[err.status] ?? fallback;
  }

  getValidationErrors(err: unknown): Record<string, string[]> | null {
    if (!(err instanceof HttpErrorResponse) || !err.error) return null;
    const body = err.error as ApiProblemDetails;
    if (body.errors && Object.keys(body.errors).length > 0) return body.errors;
    return null;
  }

  getErrorCode(err: unknown): string | null {
    if (!(err instanceof HttpErrorResponse) || !err.error) return null;
    return (err.error as ApiProblemDetails).errorCode ?? null;
  }

  handleError(err: unknown, fallback: string): void {
    const message = this.getErrorMessage(err, fallback);
    toast.error(message);
  }

  isServerOrNetworkError(err: unknown): boolean {
    if (!(err instanceof HttpErrorResponse)) return false;
    return err.status === 0 || err.status >= 500;
  }
}
