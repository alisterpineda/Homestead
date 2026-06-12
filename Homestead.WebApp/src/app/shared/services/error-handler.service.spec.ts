import { HttpErrorResponse } from '@angular/common/http';
import { ErrorHandlerService } from './error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';

describe('ErrorHandlerService', () => {
  let service: ErrorHandlerService;

  beforeEach(() => {
    service = new ErrorHandlerService();
    vi.spyOn(toast, 'error').mockImplementation(() => undefined as never);
  });

  function httpError(status: number, body?: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error: body, url: '/api/test' });
  }

  describe('getErrorMessage', () => {
    it('should return detail when present', () => {
      const err = httpError(400, { detail: 'Name is required.' });
      expect(service.getErrorMessage(err, 'fallback')).toBe('Name is required.');
    });

    it('should return first validation error when detail is absent', () => {
      const err = httpError(400, { errors: { name: ['Too short', 'Required'] } });
      expect(service.getErrorMessage(err, 'fallback')).toBe('Too short');
    });

    it('should return status-specific message for known status codes', () => {
      expect(service.getErrorMessage(httpError(404, {}), 'fallback')).toBe('The requested resource was not found.');
      expect(service.getErrorMessage(httpError(409, {}), 'fallback')).toBe('A conflict occurred. The resource may have been modified.');
      expect(service.getErrorMessage(httpError(500, {}), 'fallback')).toBe('An unexpected server error occurred. Please try again later.');
      expect(service.getErrorMessage(httpError(429, {}), 'fallback')).toBe('Too many requests. Please wait a moment and try again.');
    });

    it('should return network error message for status 0', () => {
      const err = httpError(0);
      expect(service.getErrorMessage(err, 'fallback')).toBe('Unable to reach the server. Check your connection and try again.');
    });

    it('should return fallback for unknown status codes', () => {
      const err = httpError(418, {});
      expect(service.getErrorMessage(err, 'Something went wrong.')).toBe('Something went wrong.');
    });

    it('should return fallback for non-HttpErrorResponse', () => {
      expect(service.getErrorMessage(new Error('random'), 'fallback')).toBe('fallback');
      expect(service.getErrorMessage('string error', 'fallback')).toBe('fallback');
      expect(service.getErrorMessage(null, 'fallback')).toBe('fallback');
    });

    it('should prefer detail over validation errors', () => {
      const err = httpError(400, { detail: 'Custom message', errors: { name: ['Required'] } });
      expect(service.getErrorMessage(err, 'fallback')).toBe('Custom message');
    });

    // Void-returning endpoints are generated with responseType: 'text', so their problem+json
    // body arrives as an unparsed string.
    it('should parse a string problem+json body (responseType text endpoints)', () => {
      const err = httpError(400, JSON.stringify({ detail: 'Supply has stock history and cannot be deleted.' }));
      expect(service.getErrorMessage(err, 'fallback')).toBe('Supply has stock history and cannot be deleted.');
    });

    it('should fall back to the status message for a non-JSON string body', () => {
      const err = httpError(400, '<html>Bad Request</html>');
      expect(service.getErrorMessage(err, 'fallback')).toBe('The request was invalid.');
    });
  });

  describe('getValidationErrors', () => {
    it('should return errors map when present', () => {
      const errors = { name: ['Required'], email: ['Invalid format'] };
      const err = httpError(400, { errors });
      expect(service.getValidationErrors(err)).toEqual(errors);
    });

    it('should return null when errors is empty', () => {
      const err = httpError(400, { errors: {} });
      expect(service.getValidationErrors(err)).toBeNull();
    });

    it('should parse a string problem+json body', () => {
      const errors = { name: ['Required'] };
      const err = httpError(400, JSON.stringify({ errors }));
      expect(service.getValidationErrors(err)).toEqual(errors);
    });

    it('should return null when no error body', () => {
      const err = httpError(400);
      expect(service.getValidationErrors(err)).toBeNull();
    });

    it('should return null for non-HttpErrorResponse', () => {
      expect(service.getValidationErrors(new Error('nope'))).toBeNull();
    });
  });

  describe('getErrorCode', () => {
    it('should return errorCode when present', () => {
      const err = httpError(403, { errorCode: 'account_inactive' });
      expect(service.getErrorCode(err)).toBe('account_inactive');
    });

    it('should return null when errorCode is absent', () => {
      const err = httpError(403, { detail: 'Forbidden' });
      expect(service.getErrorCode(err)).toBeNull();
    });

    it('should parse a string problem+json body', () => {
      const err = httpError(403, JSON.stringify({ errorCode: 'account_inactive' }));
      expect(service.getErrorCode(err)).toBe('account_inactive');
    });

    it('should return null for non-HttpErrorResponse', () => {
      expect(service.getErrorCode(new Error('nope'))).toBeNull();
    });
  });

  describe('handleError', () => {
    it('should call toast.error with extracted message', () => {
      const err = httpError(400, { detail: 'Bad input' });
      service.handleError(err, 'fallback');
      expect(toast.error).toHaveBeenCalledWith('Bad input');
    });

    it('should call toast.error with fallback when no detail', () => {
      const err = httpError(418, {});
      service.handleError(err, 'Something failed.');
      expect(toast.error).toHaveBeenCalledWith('Something failed.');
    });
  });

  describe('isServerOrNetworkError', () => {
    it('should return true for 5xx status codes', () => {
      expect(service.isServerOrNetworkError(httpError(500))).toBe(true);
      expect(service.isServerOrNetworkError(httpError(502))).toBe(true);
      expect(service.isServerOrNetworkError(httpError(503))).toBe(true);
      expect(service.isServerOrNetworkError(httpError(504))).toBe(true);
    });

    it('should return true for status 0 (network error)', () => {
      expect(service.isServerOrNetworkError(httpError(0))).toBe(true);
    });

    it('should return false for 4xx status codes', () => {
      expect(service.isServerOrNetworkError(httpError(400))).toBe(false);
      expect(service.isServerOrNetworkError(httpError(404))).toBe(false);
      expect(service.isServerOrNetworkError(httpError(409))).toBe(false);
      expect(service.isServerOrNetworkError(httpError(422))).toBe(false);
    });

    it('should return false for non-HttpErrorResponse', () => {
      expect(service.isServerOrNetworkError(new Error('x'))).toBe(false);
    });
  });
});
