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

function setup(items: BackupTargetResponse[]) {
  const api = new FakeApi();
  api.items = items;
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
});
