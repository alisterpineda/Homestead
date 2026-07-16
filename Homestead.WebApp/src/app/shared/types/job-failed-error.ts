/**
 * A background job reported `Failed` over SignalR. Thrown by `ProcessingHubService.waitForJob` so callers
 * (and `ErrorHandlerService`) can tell a job failure apart from an arbitrary runtime `Error`: the message
 * here is the backend's job `Error` string, which is user-appropriate by design (the admin job list renders
 * it), whereas an arbitrary `Error`'s message is internal diagnostics that must not reach a toast.
 *
 * Lives in its own file — not in processing-hub.service.ts — so the eagerly-loaded error handler can import
 * it without pulling `@microsoft/signalr` into the initial bundle.
 */
export class JobFailedError extends Error {
  constructor(
    message: string,
    readonly errorCode: string | null = null,
    readonly errorData: Record<string, string> | null = null,
  ) {
    super(message);
    this.name = 'JobFailedError';
  }
}
