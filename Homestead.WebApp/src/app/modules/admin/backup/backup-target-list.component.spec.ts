import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { BackupTargetListComponent } from './backup-target-list.component';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import type { BackupTargetResponse } from '@/api/generated/models/backup-target-response';
import type { GetBackupTargetsResponse } from '@/api/generated/models/get-backup-targets-response';

function target(overrides: Partial<BackupTargetResponse> = {}): BackupTargetResponse {
  return {
    key: 'local',
    kind: 'Local',
    location: '/mnt/nas/homestead-backup',
    isAvailable: true,
    isEnabled: true,
    backedUpFileCount: 12,
    backedUpBytes: 2048,
    ...overrides,
  };
}

class FakeApi {
  items: BackupTargetResponse[] = [target()];
  runCalls: { key: string; body: unknown }[] = [];
  patchCalls: { key: string; body: unknown }[] = [];

  listTargets() {
    return of<GetBackupTargetsResponse>({ items: this.items });
  }

  runNow(key: string, body: unknown) {
    this.runCalls.push({ key, body });
    return of({ jobId: 'job-1' });
  }

  patchTarget(key: string, body: unknown) {
    this.patchCalls.push({ key, body });
    return of({ targetKey: key, isEnabled: true });
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

  it('disables the run actions for an unavailable target', () => {
    const { element } = setup([target({ isAvailable: false, configurationError: 'Path is required.' })]);

    const runButton = element.querySelector<HTMLButtonElement>('[data-testid="back-up-now"]');
    const dryRunButton = element.querySelector<HTMLButtonElement>('[data-testid="dry-run"]');

    expect(runButton?.disabled).toBe(true);
    expect(dryRunButton?.disabled).toBe(true);
  });

  it('disables the run actions for a disabled target', () => {
    const { element } = setup([target({ isEnabled: false })]);

    expect(element.querySelector<HTMLButtonElement>('[data-testid="back-up-now"]')?.disabled).toBe(true);
  });

  it('surfaces the prune-blocked banner with the withheld count', () => {
    const { element } = setup([
      target({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 42 }),
    ]);

    const banner = element.querySelector('[data-testid="prune-blocked-banner"]');
    expect(banner).toBeTruthy();
    expect(banner?.textContent).toContain('42 deletion(s) were blocked');
    expect(element.querySelector('[data-testid="prune-anyway"]')).toBeTruthy();
  });

  it('hides the prune banner when nothing was blocked', () => {
    const { element } = setup([target()]);

    expect(element.querySelector('[data-testid="prune-blocked-banner"]')).toBeNull();
  });

  it('sends allowLargePrune when the admin prunes anyway', () => {
    const { fixture, api, element } = setup([
      target({ pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 42 }),
    ]);

    element.querySelector<HTMLButtonElement>('[data-testid="prune-anyway"]')?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([{ key: 'local', body: { dryRun: false, allowLargePrune: true } }]);
  });

  it('sends dryRun for the dry-run action', () => {
    const { fixture, api, element } = setup([target()]);

    element.querySelector<HTMLButtonElement>('[data-testid="dry-run"]')?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([{ key: 'local', body: { dryRun: true, allowLargePrune: false } }]);
  });

  it('a plain run asks for neither a dry run nor a large prune', () => {
    const { fixture, api, element } = setup([target()]);

    element.querySelector<HTMLButtonElement>('[data-testid="back-up-now"]')?.click();
    fixture.detectChanges();

    expect(api.runCalls).toEqual([{ key: 'local', body: { dryRun: false, allowLargePrune: false } }]);
  });

  // Dismissing is an acknowledgement of the latch, not a toggle of the target.
  it('dismissing the latch preserves the enabled state', () => {
    const { fixture, api, element } = setup([
      target({ isEnabled: true, pruneBlockedAt: '2026-07-01T10:00:00Z', pruneBlockedCount: 5 }),
    ]);

    element.querySelector<HTMLButtonElement>('[data-testid="dismiss-prune"]')?.click();
    fixture.detectChanges();

    expect(api.patchCalls).toEqual([{ key: 'local', body: { isEnabled: true } }]);
  });

  it('renders the last-run status and change-set summary', () => {
    const { element } = setup([
      target({
        lastRun: {
          jobId: 'job-1',
          status: 'Completed',
          createdAt: '2026-07-01T10:00:00Z',
          completedAt: '2026-07-01T10:01:00Z',
          totalChildCount: 2,
          completedChildCount: 2,
          failedChildCount: 0,
          summary: {
            targetKey: 'local',
            dryRun: false,
            putCount: 3,
            moveCount: 1,
            deleteCount: 2,
            unchangedCount: 40,
          },
        },
      }),
    ]);

    expect(element.querySelector('[data-testid="last-run-badge"]')?.textContent).toContain('Completed');
    const summary = element.querySelector('[data-testid="last-run-summary"]')?.textContent;
    expect(summary).toContain('Applied');
    expect(summary).toContain('3 to copy');
    expect(summary).toContain('1 to move');
    expect(summary).toContain('2 to remove');
    expect(summary).toContain('40 unchanged');
  });

  // The summary is the planner's staged change set, written before a single byte moves. Calling it
  // "Applied" on a run that failed would claim copies that never happened — the card would then show a
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
        summary: { targetKey: 'local', dryRun },
      }) as NonNullable<BackupTargetResponse['lastRun']>;

    expect(component.summaryVerb(run('Completed'))).toBe('Applied');
    expect(component.summaryVerb(run('Failed'))).toBe('Planned');
    expect(component.summaryVerb(run('PartiallyCompleted'))).toBe('Planned');
    expect(component.summaryVerb(run('InProgress'))).toBe('Planned');
    expect(component.summaryVerb(run('Completed', true))).toBe('Planned');
  });

  // A parent whose children failed carries no error of its own, so without the batch messages the card
  // would show a red "Failed" badge with no explanation anywhere.
  it('renders per-batch failures when the parent itself has no error', () => {
    const { element } = setup([
      target({
        lastRun: {
          jobId: 'job-1',
          status: 'Failed',
          createdAt: '2026-07-01T10:00:00Z',
          error: null,
          batchErrors: [
            "1 of 3 backup action(s) failed: Put '_undated/Resume.pdf': Source blob for version abc is missing from storage.",
          ],
          totalChildCount: 1,
          completedChildCount: 0,
          failedChildCount: 1,
        },
      }),
    ]);

    const errors = element.querySelector('[data-testid="last-run-error"]')?.textContent;
    expect(errors).toContain('Source blob for version abc is missing from storage.');
  });

  it('renders the parent error and the batch errors together', () => {
    const { fixture } = setup([target()]);

    expect(
      fixture.componentInstance.runFailures(
        target({
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

    expect(fixture.componentInstance.runFailures(target())).toEqual([]);
  });

  it('renders a skipped run as skipped, with its reason', () => {
    const { element } = setup([
      target({
        lastRun: {
          jobId: 'job-1',
          status: 'Completed',
          createdAt: '2026-07-01T10:00:00Z',
          totalChildCount: 0,
          completedChildCount: 0,
          failedChildCount: 0,
          summary: { targetKey: 'local', skipped: true, skipReason: 'Target is disabled.' },
        },
      }),
    ]);

    expect(element.querySelector('[data-testid="last-run-badge"]')?.textContent).toContain('Skipped');
    expect(element.querySelector('[data-testid="last-run-summary"]')?.textContent).toContain(
      'Target is disabled.',
    );
  });

  it('shows "Never run" before the first run', () => {
    const { element } = setup([target()]);

    expect(element.querySelector('[data-testid="last-run-badge"]')?.textContent).toContain('Never run');
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

    element.querySelector<HTMLButtonElement>('[data-testid="back-up-now"]')?.click();
    fixture.detectChanges();

    expect(hub.managed).toEqual(['job-1']);
  });

  it('notes that a schedule is paused while its target is disabled', () => {
    const { element } = setup([
      target({
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

    const note = element.querySelector('[data-testid="schedule-note"]')?.textContent;
    expect(note).toContain('paused (target disabled)');
    expect(note).not.toContain('next');
  });

  it('shows the next run when the target and its schedule are both live', () => {
    const { element } = setup([
      target({
        schedule: {
          id: 's1',
          cronExpression: '0 3 * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: '2026-07-24T03:00:00Z',
        },
      }),
    ]);

    expect(element.querySelector('[data-testid="schedule-note"]')?.textContent).toContain('next');
  });

  it('renders the failure message from the last run', () => {
    const { element } = setup([
      target({
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

    expect(element.querySelector('[data-testid="last-run-error"]')?.textContent).toContain(
      'Path is required.',
    );
  });

  it('shows no error block for a clean run', () => {
    const { element } = setup([target()]);

    expect(element.querySelector('[data-testid="last-run-error"]')).toBeNull();
  });
});
