import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { BackupTargetListComponent } from './backup-target-list.component';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import type { BackupTargetResponse } from '@/api/generated/models/backup-target-response';
import type { BackupSourceItemResponse } from '@/api/generated/models/backup-source-item-response';
import type { GetBackupTargetsResponse } from '@/api/generated/models/get-backup-targets-response';
import type { ValidateBackupTargetConnectionResponse } from '@/api/generated/models/validate-backup-target-connection-response';

const SOURCE_ID = 'src-1';

function source(overrides: Partial<BackupSourceItemResponse> = {}): BackupSourceItemResponse {
  return {
    id: SOURCE_ID,
    sourceKind: 'documents',
    sourceDisplayName: 'Documents',
    isEnabled: true,
    isAvailable: true,
    supportsPrune: true,
    backedUpFileCount: 12,
    backedUpBytes: 2048,
    ...overrides,
  };
}

function target(overrides: Partial<BackupTargetResponse> = {}): BackupTargetResponse {
  return {
    key: 'local',
    kind: 'Local',
    location: '/mnt/nas/homestead-backup',
    isAvailable: true,
    isEnabled: true,
    sources: [source()],
    ...overrides,
  };
}

/** A Google Drive card, which is the only kind that carries a connection section. */
function driveTarget(
  connection: Partial<NonNullable<BackupTargetResponse['connection']>> = {},
  overrides: Partial<BackupTargetResponse> = {},
): BackupTargetResponse {
  return target({
    key: 'gdrive',
    kind: 'GoogleDrive',
    location: 'Google Drive / Homestead/Backup',
    connection: { isConnected: true, ...connection },
    ...overrides,
  });
}

/** A target card carrying exactly one source, patched however the test needs. */
function targetWithSource(overrides: Partial<BackupSourceItemResponse>): BackupTargetResponse {
  return target({ sources: [source(overrides)] });
}

class FakeApi {
  items: BackupTargetResponse[] = [target()];
  runCalls: { id: string; body: unknown }[] = [];
  patchSourceCalls: { id: string; body: unknown }[] = [];
  patchTargetCalls: { key: string; body: unknown }[] = [];
  deleteCalls: { id: string; cancelRun: boolean | undefined }[] = [];

  /** Statuses to reply with, in order; anything left over resolves successfully. */
  deleteErrors: number[] = [];

  listTargets() {
    return of<GetBackupTargetsResponse>({
      items: this.items,
      sourceKinds: [{ key: 'documents', displayName: 'Documents', supportsPrune: true }],
    });
  }

  runNow(id: string, body: unknown) {
    this.runCalls.push({ id, body });
    return of({ jobId: 'job-1' });
  }

  patchSource(id: string, body: unknown) {
    this.patchSourceCalls.push({ id, body });
    return of({ id, isEnabled: true });
  }

  patchTarget(key: string, body: unknown) {
    this.patchTargetCalls.push({ key, body });
    return of({ targetKey: key, isEnabled: true });
  }

  deleteSource(id: string, cancelRun?: boolean) {
    this.deleteCalls.push({ id, cancelRun });

    const status = this.deleteErrors.shift();
    if (status !== undefined) {
      return throwError(() => new HttpErrorResponse({ status, error: { detail: 'nope' } }));
    }

    return of(void 0);
  }

  deleteSchedule() {
    return of(void 0);
  }

  validate(key: string) {
    this.validateCalls.push(key);
    return of(this.validateResult);
  }

  validateCalls: string[] = [];
  validateResult: ValidateBackupTargetConnectionResponse = { isAvailable: true };

  disconnect(key: string) {
    this.disconnectCalls.push(key);
    return of(void 0);
  }

  disconnectCalls: string[] = [];
}

class FakeHub {
  managed: string[] = [];

  waitForJob(jobId: string) {
    this.managed.push(jobId);
    // Never settles: these specs assert what the component asks of the hub, not what it does with the
    // eventual result.
    return new Promise<never>(() => {});
  }
}

function setup(
  items: BackupTargetResponse[],
  validateResult?: ValidateBackupTargetConnectionResponse,
) {
  const api = new FakeApi();
  api.items = items;
  if (validateResult) api.validateResult = validateResult;
  const hub = new FakeHub();

  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([]),
      { provide: AdminBackupApiService, useValue: api },
      { provide: ProcessingHubService, useValue: hub },
    ],
  });

  const fixture = TestBed.createComponent(BackupTargetListComponent);
  fixture.detectChanges();

  return { fixture, api, hub, element: fixture.nativeElement as HTMLElement };
}

/** `data-testid` values on source-scoped elements are suffixed with the source id. */
const tid = (name: string, id = SOURCE_ID) => `[data-testid="${name}-${id}"]`;

describe('BackupTargetListComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders a card per configured target', () => {
    const { element } = setup([target(), target({ key: 'usb', location: '/mnt/usb' })]);

    expect(element.querySelector('[data-testid="backup-target-local"]')).toBeTruthy();
    expect(element.querySelector('[data-testid="backup-target-usb"]')).toBeTruthy();
  });

  it('shows an empty state when no target is configured', () => {
    const { element } = setup([]);

    expect(element.querySelector('[data-testid="no-targets"]')).toBeTruthy();
  });

  it('renders a row per source under its target', () => {
    const { element } = setup([target()]);

    expect(element.querySelector(tid('backup-source'))).toBeTruthy();
    expect(element.querySelector(tid('backup-source'))?.textContent).toContain('Documents');
  });

  // A configured destination with nothing paired to it is the state right after deployment — it must
  // read as "add something", not as an error.
  it('invites a first backup when a target has no sources', () => {
    const { element } = setup([target({ sources: [] })]);

    expect(element.querySelector('[data-testid="no-sources-local"]')).toBeTruthy();
    expect(element.querySelector('[data-testid="add-backup-local"]')).toBeTruthy();
  });

  // A misconfigured target must read as "unavailable, because <reason>" — the whole point of the
  // error-string posture (no ValidateOnStart) is that the reason surfaces here.
  it('renders the configuration error for an unavailable target', () => {
    const { element } = setup([
      target({ isAvailable: false, configurationError: 'Path is required.' }),
    ]);

    expect(element.querySelector('[data-testid="configuration-error"]')?.textContent).toContain(
      'Path is required.',
    );
    expect(element.querySelector('[data-testid="availability-badge"]')?.textContent).toContain(
      'Unavailable',
    );
  });

  // A source whose kind is no longer registered is never hidden — a backup that silently stopped is
  // exactly what the admin needs to see.
  it('renders the reason a source itself is unavailable', () => {
    const { element } = setup([
      targetWithSource({
        isAvailable: false,
        sourceDisplayName: null,
        supportsPrune: false,
        unavailableReason: "Backup source kind 'finances' is no longer available in this build.",
      }),
    ]);

    expect(element.querySelector(tid('source-unavailable'))?.textContent).toContain(
      'no longer available',
    );
    // With no descriptor there is no display name, so the raw persisted kind stands in.
    expect(element.querySelector(tid('backup-source'))?.textContent).toContain('finances');
  });

  it('disables the run actions for an unavailable target', () => {
    const { element } = setup([target({ isAvailable: false, configurationError: 'Path is required.' })]);

    expect(element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.disabled).toBe(true);
    expect(element.querySelector<HTMLButtonElement>(tid('dry-run'))?.disabled).toBe(true);
  });

  it('disables the run actions for a disabled target', () => {
    const { element } = setup([target({ isEnabled: false })]);

    expect(element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.disabled).toBe(true);
  });

  it('disables the run actions for a disabled source', () => {
    const { element } = setup([targetWithSource({ isEnabled: false })]);

    expect(element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.disabled).toBe(true);
    expect(element.querySelector(tid('paused-badge'))?.textContent).toContain('Paused');
  });

  // The source's own toggle is still on, so without naming the switch that turned it off the row just
  // reads as inexplicably dead.
  it('says WHICH switch paused a row when the destination is the one disabled', () => {
    const { element } = setup([target({ isEnabled: false })]);

    expect(element.querySelector(tid('paused-badge'))?.textContent).toContain(
      'destination disabled',
    );
    expect(element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.disabled).toBe(true);
  });

  it('surfaces the prune-blocked banner with the withheld count', () => {
    const { element } = setup([
      targetWithSource({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 42 }),
    ]);

    const banner = element.querySelector(tid('prune-blocked-banner'));
    expect(banner).toBeTruthy();
    expect(banner?.textContent).toContain('42 deletion(s) were blocked');
    expect(element.querySelector(tid('prune-anyway'))).toBeTruthy();
  });

  it('hides the prune banner when nothing was blocked', () => {
    const { element } = setup([target()]);

    expect(element.querySelector(tid('prune-blocked-banner'))).toBeNull();
  });

  // A kind that never deletes has no breaker to trip, so its affordances would be nonsense.
  it('hides the prune banner for a kind that does not support pruning', () => {
    const { element } = setup([
      targetWithSource({
        supportsPrune: false,
        pruneBlockedAt: '2026-07-01T10:00:00Z',
        pruneBlockedCount: 42,
      }),
    ]);

    expect(element.querySelector(tid('prune-blocked-banner'))).toBeNull();
  });

  it('sends allowLargePrune when the admin prunes anyway', () => {
    const { fixture, api, element } = setup([
      targetWithSource({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 42 }),
    ]);

    element.querySelector<HTMLButtonElement>(tid('prune-anyway'))?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([
      { id: SOURCE_ID, body: { dryRun: false, allowLargePrune: true } },
    ]);
  });

  it('sends dryRun for the dry-run action', () => {
    const { fixture, api, element } = setup([target()]);

    element.querySelector<HTMLButtonElement>(tid('dry-run'))?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([{ id: SOURCE_ID, body: { dryRun: true, allowLargePrune: false } }]);
  });

  it('a plain run asks for neither a dry run nor a large prune', () => {
    const { fixture, api, element } = setup([target()]);

    element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([{ id: SOURCE_ID, body: { dryRun: false, allowLargePrune: false } }]);
  });

  // The latch is acknowledged EXPLICITLY, never as a side effect of some other PATCH — the banner it
  // drives is the only place a blocked prune is reported.
  it('dismissing the latch sends the acknowledgement flag and nothing else', () => {
    const { fixture, api, element } = setup([
      targetWithSource({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 5 }),
    ]);

    element.querySelector<HTMLButtonElement>(tid('dismiss-prune'))?.click();
    fixture.detectChanges();

    expect(api.patchSourceCalls).toEqual([
      { id: SOURCE_ID, body: { acknowledgePruneBlock: true } },
    ]);
  });

  it('toggling a source sends only isEnabled, leaving the latch alone', () => {
    const { fixture } = setup([
      targetWithSource({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 5 }),
    ]);
    const api = TestBed.inject(AdminBackupApiService) as unknown as FakeApi;

    fixture.componentInstance.setSourceEnabled(source(), false);

    expect(api.patchSourceCalls).toEqual([{ id: SOURCE_ID, body: { isEnabled: false } }]);
  });

  it('the destination toggle patches the target, not the source', () => {
    const { fixture, api, element } = setup([target()]);

    fixture.componentInstance.setTargetEnabled(target(), false);
    fixture.detectChanges();

    expect(api.patchTargetCalls).toEqual([{ key: 'local', body: { isEnabled: false } }]);
    expect(api.patchSourceCalls).toEqual([]);
    expect(element.querySelector('[data-testid="enabled-toggle-local"]')).toBeTruthy();
  });

  it('renders the last-run status and change-set summary', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'job-1',
          status: 'Completed',
          createdAt: '2026-07-01T10:00:00Z',
          completedAt: '2026-07-01T10:01:00Z',
          totalChildCount: 2,
          completedChildCount: 2,
          failedChildCount: 0,
          outOfSpace: false,
          summary: {
            backupSourceId: SOURCE_ID,
            dryRun: false,
            puts: 3,
            moves: 1,
            deletes: 2,
            unchanged: 40,
            totalBytes: 2048,
          },
        },
      }),
    ]);

    expect(element.querySelector(tid('last-run-badge'))?.textContent).toContain('Completed');
    const summary = element.querySelector(tid('last-run-summary'))?.textContent;
    expect(summary).toContain('Applied');
    expect(summary).toContain('3 to copy');
    expect(summary).toContain('1 to move');
    expect(summary).toContain('2 to remove');
    expect(summary).toContain('40 unchanged');
    expect(summary).toContain('2 KB');
  });

  // Null bytes means "the planner could not know at plan time", which is not the same as "nothing to
  // copy" — rendering it as 0 B would read as the latter.
  it('renders unknown byte totals as an em dash, never "0 B"', () => {
    const { fixture } = setup([target()]);
    const component = fixture.componentInstance;

    expect(component.summaryBytes(null)).toBe('—');
    expect(component.summaryBytes(undefined)).toBe('—');
    expect(component.summaryBytes(0)).toBe('0 B');
    expect(component.summaryBytes(2048)).toBe('2 KB');
  });

  // The summary is the planner's staged change set, written before a single byte moves. Calling it
  // "Applied" on a run that failed would claim copies that never happened — the row would then show a
  // red badge next to a green-sounding count, which is exactly how a broken backup reads as a working one.
  it('never calls the change set "Applied" for a run that did not complete', () => {
    const { fixture } = setup([target()]);
    const component = fixture.componentInstance;

    const run = (status: string, dryRun = false) =>
      ({
        jobId: 'job-1',
        status,
        createdAt: '2026-07-01T10:00:00Z',
        totalChildCount: 1,
        completedChildCount: 0,
        failedChildCount: 1,
        outOfSpace: false,
        summary: { backupSourceId: SOURCE_ID, dryRun },
      }) as NonNullable<BackupSourceItemResponse['lastRun']>;

    expect(component.summaryVerb(run('Completed'))).toBe('Applied');
    expect(component.summaryVerb(run('Failed'))).toBe('Planned');
    expect(component.summaryVerb(run('PartiallyCompleted'))).toBe('Planned');
    expect(component.summaryVerb(run('InProgress'))).toBe('Planned');
    expect(component.summaryVerb(run('Cancelled'))).toBe('Planned');
    expect(component.summaryVerb(run('Completed', true))).toBe('Planned');
  });

  // A parent whose children failed carries no error of its own, so without the batch messages the row
  // would show a red "Failed" badge with no explanation anywhere.
  it('renders per-batch failures when the parent itself has no error', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'job-1',
          status: 'Failed',
          createdAt: '2026-07-01T10:00:00Z',
          error: null,
          batchErrors: [
            "1 of 3 backup action(s) failed: Put 'Documents/_undated/Resume.pdf': Source blob for version abc is missing from storage.",
          ],
          totalChildCount: 1,
          completedChildCount: 0,
          failedChildCount: 1,
          outOfSpace: false,
        },
      }),
    ]);

    expect(element.querySelector(tid('last-run-error'))?.textContent).toContain(
      'Source blob for version abc is missing from storage.',
    );
  });

  it('renders the parent error and the batch errors together', () => {
    const { fixture } = setup([target()]);

    expect(
      fixture.componentInstance.runFailures(
        source({
          lastRun: {
            jobId: 'job-1',
            status: 'Failed',
            createdAt: '2026-07-01T10:00:00Z',
            error: 'planner blew up',
            batchErrors: ['batch one failed', 'batch two failed'],
            totalChildCount: 2,
            completedChildCount: 0,
            failedChildCount: 2,
            outOfSpace: false,
          },
        }),
      ),
    ).toEqual(['planner blew up', 'batch one failed', 'batch two failed']);
  });

  it('reports no failures for a clean run', () => {
    const { fixture } = setup([target()]);

    expect(fixture.componentInstance.runFailures(source())).toEqual([]);
  });

  it('renders a skipped run as skipped, with its reason', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'job-1',
          status: 'Completed',
          createdAt: '2026-07-01T10:00:00Z',
          totalChildCount: 0,
          completedChildCount: 0,
          failedChildCount: 0,
          outOfSpace: false,
          summary: {
            backupSourceId: SOURCE_ID,
            skipped: true,
            skipReason: 'Backup source is disabled.',
          },
        },
      }),
    ]);

    expect(element.querySelector(tid('last-run-badge'))?.textContent).toContain('Skipped');
    expect(element.querySelector(tid('last-run-summary'))?.textContent).toContain(
      'Backup source is disabled.',
    );
  });

  it('shows "Never run" before the first run', () => {
    const { element } = setup([target()]);

    expect(element.querySelector(tid('last-run-badge'))?.textContent).toContain('Never run');
  });

  it('labels a cancelled run as cancelled', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'job-1',
          status: 'Cancelled',
          createdAt: '2026-07-01T10:00:00Z',
          completedAt: '2026-07-01T10:00:30Z',
          totalChildCount: 3,
          completedChildCount: 1,
          failedChildCount: 0,
          outOfSpace: false,
        },
      }),
    ]);

    expect(element.querySelector(tid('last-run-badge'))?.textContent).toContain('Cancelled');
  });

  it('labels a partially-completed run with its failure count', () => {
    const { fixture } = setup([target()]);

    expect(
      fixture.componentInstance.statusLabel({
        jobId: 'job-1',
        status: 'PartiallyCompleted',
        createdAt: '2026-07-01T10:00:00Z',
        totalChildCount: 5,
        completedChildCount: 4,
        failedChildCount: 1,
        outOfSpace: false,
      }),
    ).toBe('Partial (1 failed)');
  });

  it('maps job statuses to badge variants', () => {
    const { fixture } = setup([target()]);
    const component = fixture.componentInstance;

    expect(component.statusVariant('Completed')).toBe('default');
    expect(component.statusVariant('Failed')).toBe('destructive');
    expect(component.statusVariant('PartiallyCompleted')).toBe('secondary');
    expect(component.statusVariant('Pending')).toBe('outline');
    expect(component.statusVariant('Cancelled')).toBe('outline');
  });

  it('formats byte totals in human units', () => {
    const { fixture } = setup([target()]);
    const component = fixture.componentInstance;

    // Delegates to the shared formatBytes (strips trailing zeros), with a guard for the negative case.
    expect(component.formatBytes(0)).toBe('0 B');
    expect(component.formatBytes(-1)).toBe('0 B');
    expect(component.formatBytes(512)).toBe('512 B');
    expect(component.formatBytes(2048)).toBe('2 KB');
    expect(component.formatBytes(2560)).toBe('2.5 KB');
    expect(component.formatBytes(5 * 1024 * 1024)).toBe('5 MB');
  });

  // The hub's generic completion toast is worded for document uploads and renders as
  // `"Document" has been processed successfully.` for a backup, which carries no document name.
  // Claiming the job is what suppresses it.
  it('claims the run on the processing hub so the generic document toast is suppressed', () => {
    const { fixture, hub, element } = setup([target()]);

    element.querySelector<HTMLButtonElement>(tid('back-up-now'))?.click();
    fixture.detectChanges();

    expect(hub.managed).toEqual(['job-1']);
  });

  it('notes that a schedule is paused while its source is disabled', () => {
    const { element } = setup([
      targetWithSource({
        isEnabled: false,
        schedule: {
          id: 's1',
          cronExpression: '0 3 * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: '2026-07-24T03:00:00Z',
        },
      }),
    ]);

    const note = element.querySelector(tid('schedule-note'))?.textContent;
    expect(note).toContain('paused (backup disabled)');
    expect(note).not.toContain('next');
  });

  it('notes that a schedule is paused while its destination is disabled', () => {
    const { element } = setup([
      target({
        isEnabled: false,
        sources: [
          source({
            schedule: {
              id: 's1',
              cronExpression: '0 3 * * *',
              isEnabled: true,
              priority: 0,
              nextRunAt: '2026-07-24T03:00:00Z',
            },
          }),
        ],
      }),
    ]);

    expect(element.querySelector(tid('schedule-note'))?.textContent).toContain(
      'paused (destination disabled)',
    );
  });

  it('shows the next run when the destination, source and schedule are all live', () => {
    const { element } = setup([
      targetWithSource({
        schedule: {
          id: 's1',
          cronExpression: '0 3 * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: '2026-07-24T03:00:00Z',
        },
      }),
    ]);

    expect(element.querySelector(tid('schedule-note'))?.textContent).toContain('next');
  });

  it('renders the failure message from the last run', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'job-1',
          status: 'Failed',
          createdAt: '2026-07-01T10:00:00Z',
          error: "Backup target 'local' is unavailable: Path is required.",
          totalChildCount: 0,
          completedChildCount: 0,
          failedChildCount: 0,
          outOfSpace: false,
        },
      }),
    ]);

    expect(element.querySelector(tid('last-run-error'))?.textContent).toContain('Path is required.');
  });

  it('shows no error block for a clean run', () => {
    const { element } = setup([target()]);

    expect(element.querySelector(tid('last-run-error'))).toBeNull();
  });

  // --- delete, which escalates rather than guessing ---

  it('the first delete attempt never cancels a run', () => {
    const { fixture, api } = setup([target()]);

    fixture.componentInstance.deleteSource(source(), { close: () => {} });

    expect(api.deleteCalls).toEqual([{ id: SOURCE_ID, cancelRun: undefined }]);
  });

  it('a 409 turns the confirmation into an offer to cancel the run', () => {
    const { fixture, api } = setup([target()]);
    api.deleteErrors = [409];

    const component = fixture.componentInstance;
    component.deleteSource(source(), { close: () => {} });

    expect(component.deleteStage()).toBe('cancelRun');

    // The retry now opts in — and only now.
    component.deleteSource(source(), { close: () => {} });
    expect(api.deleteCalls).toEqual([
      { id: SOURCE_ID, cancelRun: undefined },
      { id: SOURCE_ID, cancelRun: true },
    ]);
  });

  // A batch already inside its handler cannot be cancelled by anything, so the only honest answer is
  // "come back in a moment" — with the action still enabled.
  it('a second 409 after opting in becomes a retry', () => {
    const { fixture, api } = setup([target()]);
    api.deleteErrors = [409, 409];

    const component = fixture.componentInstance;
    component.deleteSource(source(), { close: () => {} });
    component.deleteSource(source(), { close: () => {} });

    expect(component.deleteStage()).toBe('retry');
    expect(component.deleting()).toBe(false);
  });

  it('reopening the dialog resets it to the plain confirmation', () => {
    const { fixture, api } = setup([target()]);
    api.deleteErrors = [409];

    const component = fixture.componentInstance;
    component.deleteSource(source(), { close: () => {} });
    expect(component.deleteStage()).toBe('cancelRun');

    component.onDeleteDialogState(source(), 'open');

    expect(component.deleteStage()).toBe('confirm');
  });

  it('a non-conflict failure closes the dialog instead of escalating', () => {
    const { fixture, api } = setup([target()]);
    api.deleteErrors = [500];

    let closed = false;
    const component = fixture.componentInstance;
    component.deleteSource(source(), { close: () => (closed = true) });

    expect(closed).toBe(true);
    expect(component.deleteStage()).toBe('confirm');
  });
  // --- Connection section ---------------------------------------------------------------------------

  it('renders no connection section for a local directory', () => {
    const { element } = setup([target()]);

    expect(element.querySelector('[data-testid="connection-local"]')).toBeNull();
  });

  it('offers Connect on a Drive target with no stored credential', () => {
    const { element } = setup([driveTarget({ isConnected: false })]);

    expect(element.querySelector('[data-testid="connection-gdrive"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="connect-google-gdrive"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="connected-as-gdrive"]')).toBeNull();
  });

  it('names the connected account and offers Disconnect', () => {
    const { element } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' })]);

    expect(element.querySelector('[data-testid="connected-as-gdrive"]')?.textContent).toContain(
      'owner@example.test',
    );
    expect(element.querySelector('[data-testid="disconnect-gdrive"]')).not.toBeNull();
  });

  it('shows the latch reason with a Reconnect affordance', () => {
    const { element } = setup(
      [
        driveTarget({
          connectedAccountEmail: 'owner@example.test',
          authFailedAt: '2026-07-01T00:00:00Z',
          authFailedReason: 'Publish your consent screen to In production.',
        }),
      ],
      { isAvailable: false, reason: 'Publish your consent screen to In production.' },
    );

    const banner = element.querySelector('[data-testid="auth-failed-banner-gdrive"]');
    expect(banner?.textContent).toContain('Publish your consent screen');
  });

  // A probe that proves the credential works makes the stored latch stale — the banner must not linger
  // past the evidence that it is wrong.
  it('drops a stale latch banner once the probe succeeds', () => {
    const { element } = setup(
      [
        driveTarget({
          connectedAccountEmail: 'owner@example.test',
          authFailedAt: '2026-07-01T00:00:00Z',
          authFailedReason: 'Publish your consent screen to In production.',
        }),
      ],
      { isAvailable: true },
    );

    expect(element.querySelector('[data-testid="auth-failed-banner-gdrive"]')).toBeNull();
  });

  it('disconnects through the API', () => {
    const { fixture, api } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' })]);

    let closed = false;
    fixture.componentInstance.disconnect(driveTarget(), { close: () => (closed = true) });

    expect(api.disconnectCalls).toEqual(['gdrive']);
    expect(closed).toBe(true);
  });

  // --- The post-render validate probe ----------------------------------------------------------------

  // The list endpoint is a pure config+DB read; proving the credential still works is a separate call
  // fired after render so the page never waits on Google.
  it('validates every connected Drive target after load', () => {
    const { api } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' }), target()]);

    expect(api.validateCalls).toEqual(['gdrive']);
  });

  it('does not validate a Drive target that has nothing to validate', () => {
    const { api } = setup([driveTarget({ isConnected: false })]);

    expect(api.validateCalls).toEqual([]);
  });

  it("the probe's verdict supersedes the list endpoint's badge", () => {
    const { element } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' })], {
      isAvailable: false,
      reason: 'Google revoked the authorization.',
    });

    expect(element.querySelector('[data-testid="availability-badge"]')?.textContent).toContain(
      'Unavailable',
    );
    expect(element.querySelector('[data-testid="configuration-error"]')?.textContent).toContain(
      'Google revoked',
    );
  });

  it('renders free space once the probe answers', () => {
    const { element } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' })], {
      isAvailable: true,
      storageUsedBytes: 5_000_000_000,
      storageLimitBytes: 15_000_000_000,
    });

    expect(element.querySelector('[data-testid="free-space-gdrive"]')?.textContent).toContain(
      'free of',
    );
  });

  // Null limit means unlimited, which is a real state and not zero.
  it('reads an absent quota limit as unlimited rather than full', () => {
    const { fixture } = setup([driveTarget({ connectedAccountEmail: 'owner@example.test' })]);

    fixture.componentInstance.validation.set({
      gdrive: { isAvailable: true, storageUsedBytes: 100, storageLimitBytes: null },
    });

    expect(fixture.componentInstance.freeSpaceLabel(driveTarget())).toContain('unlimited');
  });

  // --- Staleness ------------------------------------------------------------------------------------

  it('reads never-succeeded as "never"', () => {
    const { element } = setup([targetWithSource({ lastSuccessfulRunAt: null })]);

    expect(element.querySelector(tid('last-success'))?.textContent).toContain('never');
  });

  it('renders a relative age for the last success', () => {
    const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString();
    const { element } = setup([targetWithSource({ lastSuccessfulRunAt: twoDaysAgo })]);

    expect(element.querySelector(tid('last-success'))?.textContent).toContain('2d ago');
  });

  // A manual-only backup is as stale as its owner intends — flagging it would be noise.
  it('never flags staleness without an armed schedule', () => {
    const ancient = new Date(Date.now() - 400 * 24 * 60 * 60 * 1000).toISOString();
    const { fixture } = setup([targetWithSource({ lastSuccessfulRunAt: ancient })]);

    expect(fixture.componentInstance.isStale(source({ lastSuccessfulRunAt: ancient }))).toBe(false);
  });

  // Scaled to the schedule — roughly two missed runs — because "3 days ago" is alarming for an hourly
  // backup and unremarkable for a weekly one.
  it('flags staleness past about two missed scheduled runs', () => {
    const { fixture } = setup([target()]);
    const component = fixture.componentInstance;

    const hourly = {
      id: 's',
      cronExpression: '0 * * * *',
      isEnabled: true,
      priority: 0,
      lastRunAt: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
      nextRunAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    };

    const fresh = new Date(Date.now() - 30 * 60 * 1000).toISOString();
    const stale = new Date(Date.now() - 5 * 60 * 60 * 1000).toISOString();

    expect(component.isStale(source({ schedule: hourly, lastSuccessfulRunAt: fresh }))).toBe(false);
    expect(component.isStale(source({ schedule: hourly, lastSuccessfulRunAt: stale }))).toBe(true);
  });

  it('never flags staleness while the schedule is disabled', () => {
    const { fixture } = setup([target()]);

    const disabled = {
      id: 's',
      cronExpression: '0 * * * *',
      isEnabled: false,
      priority: 0,
      lastRunAt: new Date(Date.now() - 60 * 60 * 1000).toISOString(),
      nextRunAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    };

    const ancient = new Date(Date.now() - 400 * 24 * 60 * 60 * 1000).toISOString();

    expect(
      fixture.componentInstance.isStale(source({ schedule: disabled, lastSuccessfulRunAt: ancient })),
    ).toBe(false);
  });

  // --- Out of space ---------------------------------------------------------------------------------

  // One rolled-up sentence, not two hundred identical batch fragments — and decided from the typed
  // marker, never by matching message strings.
  it('renders one message when the destination is full', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'j1',
          status: 'Failed',
          createdAt: '2026-07-01T00:00:00Z',
          totalChildCount: 3,
          completedChildCount: 1,
          failedChildCount: 2,
          outOfSpace: true,
          batchErrors: [
            'Google Drive is out of storage space — mirror deletes free space only after ' +
              "Google's trash is emptied.",
            'Google Drive is out of storage space — mirror deletes free space only after ' +
              "Google's trash is emptied.",
          ],
        },
      }),
    ]);

    // One rolled-up sentence, sourced from the destination rather than restated here.
    const message = element.querySelector(tid('out-of-space'))?.textContent ?? '';
    expect(message).toContain('out of storage space');
    expect(message).toContain("Google's trash");
    expect(element.querySelector(tid('last-run-error'))).toBeNull();
  });

  it('still stacks ordinary batch errors', () => {
    const { element } = setup([
      targetWithSource({
        lastRun: {
          jobId: 'j1',
          status: 'Failed',
          createdAt: '2026-07-01T00:00:00Z',
          totalChildCount: 1,
          completedChildCount: 0,
          failedChildCount: 1,
          outOfSpace: false,
          batchErrors: ['Put failed'],
        },
      }),
    ]);

    expect(element.querySelector(tid('out-of-space'))).toBeNull();
    expect(element.querySelector(tid('last-run-error'))?.textContent).toContain('Put failed');
  });

  it('labels the Google Drive kind', () => {
    const { element } = setup([driveTarget()]);

    expect(element.textContent).toContain('Google Drive');
  });

  // --- The location ---------------------------------------------------------------------------------

  // The only workable answer to "which of my two Homestead folders is the app's": the drive.file scope
  // cannot see a folder the app did not create, so a collision can never be detected, let alone warned
  // about. One click settles it permanently.
  it('links the location to the destination folder when it has a URL', () => {
    const { element } = setup([
      driveTarget({}, { locationUrl: 'https://drive.google.com/drive/folders/backup-id' }),
    ]);

    const link = element.querySelector<HTMLAnchorElement>('[data-testid="location-link-gdrive"]');

    expect(link?.getAttribute('href')).toBe('https://drive.google.com/drive/folders/backup-id');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toContain('noopener');
    expect(link?.textContent).toContain('Google Drive / Homestead/Backup');
  });

  // A local directory has no browsable URL, and neither does a Drive target before its root exists.
  it('renders the location as plain text with no URL', () => {
    const { element } = setup([target()]);

    expect(element.querySelector('[data-testid="location-link-local"]')).toBeNull();
    expect(element.querySelector('[data-testid="backup-target-local"]')?.textContent).toContain(
      '/mnt/nas/homestead-backup',
    );
  });

  // Reading the location from the database is what makes it true — but it also turns a config edit into a
  // silent no-op, and a setting that does nothing while nothing says so is the same trap in a new place.
  it('surfaces a config-vs-actual skew notice when the server sends one', () => {
    const { element } = setup([
      driveTarget(
        {},
        {
          locationNotice:
            "Config asks for 'Archive/Homestead', but the mirror was created at 'Homestead/Backup' and stays there.",
        },
      ),
    ]);

    expect(element.querySelector('[data-testid="location-notice-gdrive"]')?.textContent).toContain(
      "Config asks for 'Archive/Homestead'",
    );
  });

  // The common case — config unset, the default used — must render nothing at all.
  it('renders no location notice when config and the created root agree', () => {
    const { element } = setup([driveTarget()]);

    expect(element.querySelector('[data-testid="location-notice-gdrive"]')).toBeNull();
  });
});
