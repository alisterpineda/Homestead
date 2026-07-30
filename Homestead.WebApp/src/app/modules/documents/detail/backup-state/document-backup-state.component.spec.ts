import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DocumentBackupStateComponent } from './document-backup-state.component';
import type { DocumentBackupSourceState } from '@/api/generated/models/document-backup-source-state';
import type { DocumentBackupVersionState } from '@/api/generated/models/document-backup-version-state';

const SOURCE_ID = 'src-1';
const VERSION_ID = 'ver-2';
const OLDER_VERSION_ID = 'ver-1';

function version(overrides: Partial<DocumentBackupVersionState> = {}): DocumentBackupVersionState {
  return {
    documentVersionId: VERSION_ID,
    versionNumber: 2,
    ...overrides,
  };
}

function source(overrides: Partial<DocumentBackupSourceState> = {}): DocumentBackupSourceState {
  return {
    backupSourceId: SOURCE_ID,
    targetKey: 'local',
    targetKind: 'Local',
    sourceDisplayName: 'Documents',
    isAvailable: true,
    isEnabled: true,
    targetEnabled: true,
    copiedVersionCount: 1,
    pendingRemovalCount: 0,
    versions: [version({ lastWrittenAt: new Date().toISOString() })],
    ...overrides,
  };
}

/** Host, because the component's inputs are signal inputs and one of them is required. */
@Component({
  imports: [DocumentBackupStateComponent],
  template: `
    <app-document-backup-state [sources]="sources()" [selectedVersionId]="selectedVersionId()" />
  `,
})
class HostComponent {
  sources = signal<DocumentBackupSourceState[]>([]);
  selectedVersionId = signal<string | null>(null);
}

function setup(sources: DocumentBackupSourceState[], selectedVersionId: string | null = null) {
  TestBed.configureTestingModule({ imports: [HostComponent] });

  const fixture = TestBed.createComponent(HostComponent);
  fixture.componentInstance.sources.set(sources);
  fixture.componentInstance.selectedVersionId.set(selectedVersionId);
  fixture.detectChanges();

  return { fixture, element: fixture.nativeElement as HTMLElement };
}

const tid = (name: string, id = SOURCE_ID) => `[data-testid="${name}-${id}"]`;
const text = (element: HTMLElement, selector: string) =>
  element.querySelector(selector)?.textContent ?? '';

describe('DocumentBackupStateComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders one block per pairing', () => {
    const { element } = setup([
      source(),
      source({ backupSourceId: 'src-2', targetKey: 'gdrive', targetKind: 'GoogleDrive' }),
    ]);

    expect(element.querySelector(tid('backup-source'))).toBeTruthy();
    expect(element.querySelector(tid('backup-source', 'src-2'))).toBeTruthy();
    expect(text(element, tid('backup-source', 'src-2'))).toContain('Google Drive');
  });

  it('reports when the version was last written, not that it was copied', () => {
    const { element } = setup([source()]);

    const written = text(element, tid('version-written'));
    expect(written).toContain('last written');
    expect(written).not.toContain('Copied');
  });

  // Availability leads: disconnecting a target keeps its state rows, so a copy record rendered without
  // the verdict would read as reassuring about a destination nobody can reach.
  it('leads with the reason a pairing is unavailable', () => {
    const { element } = setup([
      source({
        isAvailable: false,
        unavailableReason: 'Google revoked the authorization — reconnect.',
      }),
    ]);

    expect(text(element, tid('availability-badge'))).toContain('Unavailable');
    expect(text(element, tid('unavailable-reason'))).toContain('reconnect');
  });

  it('marks a paused pairing without calling it broken', () => {
    const { element } = setup([source({ isEnabled: false })]);

    expect(text(element, tid('availability-badge'))).toContain('Paused');
    expect(element.querySelector(tid('unavailable-reason'))).toBeFalsy();
  });

  it('renders "never" when the pairing has never had a successful run', () => {
    const { element } = setup([source({ lastSuccessfulRunAt: null })]);

    expect(text(element, tid('last-success'))).toContain('never');
  });

  // The catch-all: scaled to the schedule, so "3 days ago" is alarming for an hourly backup and
  // unremarkable for a weekly one.
  it('flags a stale last-successful-run as destructive', () => {
    const hourAgo = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    const inAnHour = new Date(Date.now() + 60 * 60 * 1000).toISOString();

    const { element } = setup([
      source({
        lastSuccessfulRunAt: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
        schedule: {
          id: 'sch-1',
          cronExpression: '0 * * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: inAnHour,
          lastRunAt: hourAgo,
        },
      }),
    ]);

    expect(element.querySelector(tid('last-success'))?.className).toContain('text-destructive');
  });

  // Never a bare negative — the reason is what tells a reader whether to wait or to act.
  it('explains an unwritten version rather than just saying it is missing', () => {
    const { element } = setup([
      source({ copiedVersionCount: 0, versions: [version()] }),
    ]);

    expect(text(element, tid('version-not-written'))).toContain(
      'there is no schedule and this backup has never run',
    );
  });

  it('names the next run when a schedule is armed', () => {
    const { element } = setup([
      source({
        copiedVersionCount: 0,
        versions: [version()],
        schedule: {
          id: 'sch-1',
          cronExpression: '0 3 * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        },
      }),
    ]);

    expect(text(element, tid('version-not-written'))).toContain('the next run is');
  });

  // Absence of a recorded failure is not success, and must never fall through to a schedule sentence
  // that reads as "all fine, just waiting".
  it('says the outcome was not recorded when the run failed without naming this file', () => {
    const { element } = setup([
      source({
        copiedVersionCount: 0,
        versions: [version()],
        lastRun: {
          jobId: 'job-1',
          status: 'Failed',
          createdAt: new Date().toISOString(),
          totalChildCount: 1,
          completedChildCount: 0,
          failedChildCount: 1,
          outOfSpace: false,
        },
        schedule: {
          id: 'sch-1',
          cronExpression: '0 3 * * *',
          isEnabled: true,
          priority: 0,
          nextRunAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        },
      }),
    ]);

    const notWritten = text(element, tid('version-not-written'));
    expect(notWritten).toContain("this file's outcome was not recorded");
    expect(notWritten).not.toContain('the next run is');
  });

  it('shows this version’s own failure message when one was recorded', () => {
    const { element } = setup([
      source({
        copiedVersionCount: 0,
        versions: [
          version({
            lastFailureMessage: 'Source blob is missing from storage.',
            lastFailureAt: new Date().toISOString(),
          }),
        ],
      }),
    ]);

    expect(text(element, tid('version-failure'))).toContain('Source blob is missing from storage.');
  });

  // The trap this panel exists to avoid, in its most reachable form: a version copied by an earlier run
  // and then FAILED by the newest one carries both facts, and a rename whose move fails produces exactly
  // that shape. Rendering only the older, cheerful timestamp is "absence of failure reads as success"
  // with the failure right there in the payload.
  it('still shows the failure when the version also has an older written record', () => {
    const { element } = setup([
      source({
        versions: [
          version({
            lastWrittenAt: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString(),
            remoteRelativePath: 'Documents/2025/Lease.pdf',
            lastFailureMessage: 'The destination rejected the move.',
            lastFailureAt: new Date().toISOString(),
          }),
        ],
      }),
    ]);

    // Both, not either: the copy record is still true, it is just no longer the whole story.
    expect(text(element, tid('version-written'))).toContain('Documents/2025/Lease.pdf');

    const failure = text(element, tid('version-failure'));
    expect(failure).toContain('The destination rejected the move.');
    expect(failure).toContain('from an earlier run');
  });

  it('reports on the selected version rather than the newest', () => {
    const { element } = setup(
      [
        source({
          copiedVersionCount: 1,
          versions: [
            version({ lastWrittenAt: new Date().toISOString() }),
            version({
              documentVersionId: OLDER_VERSION_ID,
              versionNumber: 1,
              lastFailureMessage: 'Content hash mismatch.',
            }),
          ],
        }),
      ],
      OLDER_VERSION_ID,
    );

    expect(text(element, tid('version-failure'))).toContain('Content hash mismatch.');
  });

  // Tombstone rows outlive the version they describe: the leftover file is genuinely still in the
  // mirror, and the roll-up must not fold it into the copied count.
  it('reports files awaiting removal separately from copies', () => {
    const { element } = setup([source({ copiedVersionCount: 1, pendingRemovalCount: 2 })]);

    const rollup = text(element, tid('version-rollup'));
    expect(rollup).toContain('1 of 1 version(s) recorded here.');
    expect(rollup).toContain('2 file(s) from deleted versions');
  });

  it('links to the mirrored object and names the connected account', () => {
    const { element } = setup([
      source({
        targetKind: 'GoogleDrive',
        targetAccountEmail: 'backups@example.com',
        versions: [
          version({
            lastWrittenAt: new Date().toISOString(),
            remoteRelativePath: 'Documents/2025/Lease.pdf',
            remoteUrl: 'https://drive.google.com/file/d/file-1/view',
          }),
        ],
      }),
    ]);

    const link = element.querySelector<HTMLAnchorElement>(tid('remote-link'));
    expect(link?.getAttribute('href')).toBe('https://drive.google.com/file/d/file-1/view');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.textContent).toContain('backups@example.com');
  });

  // The server suppresses the URL for an unusable pairing; the panel must not invent one.
  it('renders no link when the server sent no object URL', () => {
    const { element } = setup([
      source({
        versions: [
          version({
            lastWrittenAt: new Date().toISOString(),
            remoteRelativePath: 'Documents/2025/Lease.pdf',
          }),
        ],
      }),
    ]);

    expect(element.querySelector(tid('remote-link'))).toBeFalsy();
    expect(text(element, tid('version-written'))).toContain('Documents/2025/Lease.pdf');
  });

  it('says plainly that nothing here is proof the copy survives', () => {
    const { element } = setup([source()]);

    expect(element.textContent).toContain('never read back');
  });
});
