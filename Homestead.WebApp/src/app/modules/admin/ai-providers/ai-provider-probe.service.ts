import { Injectable, inject } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { toast } from '@spartan-ng/brain/sonner';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ListModelsResponse } from '@/api/generated/models/list-models-response';
import type { TestConnectionResponse } from '@/api/generated/models/test-connection-response';
import type { AiProviderTestResult } from './ai-provider-config';

// Centralizes the fetch-models / test-connection result handling (toasts + message derivation) shared
// by the create dialog and edit sheet. Callers pass the already-built API call (ad-hoc or by-id) and
// drive their own loading signals from the returned stream — keeping the toast wording and the
// success/failure derivation in one place so the two components can't drift.
@Injectable({ providedIn: 'root' })
export class AiProviderProbeService {
  private errorHandler = inject(ErrorHandlerService);

  // Emits the model list; toasts on empty, and toasts + rethrows on error so the caller can clear its
  // loading flag in its error handler.
  fetchModels(call$: Observable<ListModelsResponse>): Observable<string[]> {
    return call$.pipe(
      map(res => res.models),
      tap(models => {
        if (models.length === 0) {
          toast.info('No models returned for this provider.');
        }
      }),
      catchError(err => {
        toast.error(this.errorHandler.getErrorMessage(err, 'Failed to fetch models.'));
        return throwError(() => err);
      }),
    );
  }

  // Always emits a result (errors are mapped to success:false) so the caller subscribes with a single
  // next handler; toasts success or failure either way.
  testConnection(call$: Observable<TestConnectionResponse>): Observable<AiProviderTestResult> {
    return call$.pipe(
      map(res => ({
        success: res.success,
        message: res.message ?? (res.success ? 'Connection successful.' : 'Connection failed.'),
      })),
      tap(result => result.success ? toast.success(result.message) : toast.error(result.message)),
      catchError(err => {
        const message = this.errorHandler.getErrorMessage(err, 'Failed to test connection.');
        toast.error(message);
        return of<AiProviderTestResult>({ success: false, message });
      }),
    );
  }
}
