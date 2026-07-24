import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AddBackupDialogComponent } from './add-backup-dialog.component';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import type { BackupSourceKindResponse } from '@/api/generated/models/backup-source-kind-response';

const KINDS: BackupSourceKindResponse[] = [
  { key: 'documents', displayName: 'Documents', supportsPrune: true },
  { key: 'finances', displayName: 'Finances', supportsPrune: false },
];

class FakeApi {
  calls: unknown[] = [];
  failWith: number | null = null;

  createSource(body: unknown) {
    this.calls.push(body);

    if (this.failWith !== null) {
      return throwError(
        () =>
          new HttpErrorResponse({
            status: this.failWith!,
            error: { detail: "'Documents' is already backed up to 'local'." },
          }),
      );
    }

    return of({ id: 'src-1', sourceKind: 'documents', targetKey: 'local' });
  }
}

function setup(existingKinds: string[] = [], kinds: BackupSourceKindResponse[] = KINDS) {
  const api = new FakeApi();

  TestBed.configureTestingModule({
    providers: [provideHttpClient(), { provide: AdminBackupApiService, useValue: api }],
  });

  const fixture = TestBed.createComponent(AddBackupDialogComponent);
  fixture.componentRef.setInput('targetKey', 'local');
  fixture.componentRef.setInput('sourceKinds', kinds);
  fixture.componentRef.setInput('existingKinds', existingKinds);
  fixture.detectChanges();

  return { fixture, api, component: fixture.componentInstance };
}

describe('AddBackupDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('offers every registered kind when nothing is paired yet', () => {
    const { component } = setup();

    expect(component.available().map(k => k.key)).toEqual(['documents', 'finances']);
  });

  // One pairing per kind per target is the invariant; filtering is the friendly half of enforcing it.
  it('hides kinds already paired with this target', () => {
    const { component } = setup(['documents']);

    expect(component.available().map(k => k.key)).toEqual(['finances']);
  });

  it('matches existing kinds case-insensitively', () => {
    const { component } = setup(['DOCUMENTS']);

    expect(component.available().map(k => k.key)).toEqual(['finances']);
  });

  it('cannot be opened when everything is already paired', () => {
    const { component } = setup(['documents', 'finances']);

    expect(component.available()).toEqual([]);
  });

  // With a single kind registered, making the admin open a select of one is pure friction.
  it('pre-selects the only remaining option on open', () => {
    const { component } = setup(['documents']);

    component.onStateChanged('open');

    expect(component.sourceKind()).toBe('finances');
    expect(component.canSubmit()).toBe(true);
  });

  it('leaves the choice empty when more than one option remains', () => {
    const { component } = setup();

    component.onStateChanged('open');

    expect(component.sourceKind()).toBe('');
    expect(component.canSubmit()).toBe(false);
  });

  it('creates the pairing against the card it was opened from', () => {
    const { component, api } = setup();

    component.sourceKind.set('documents');
    component.handleSubmit(new Event('submit'));

    expect(api.calls).toEqual([{ sourceKind: 'documents', targetKey: 'local' }]);
  });

  it('does not submit without a choice', () => {
    const { component, api } = setup();

    component.onStateChanged('open');
    component.handleSubmit(new Event('submit'));

    expect(api.calls).toEqual([]);
  });

  // The server's 409 is the real guarantee (a unique index backs it); a race that slips past the
  // filtered picker has to land somewhere the admin can read it.
  it('shows the duplicate conflict inline', () => {
    const { component, api } = setup();
    api.failWith = 409;

    component.sourceKind.set('documents');
    component.handleSubmit(new Event('submit'));

    expect(component.addError()).toContain('already backed up');
    expect(component.adding()).toBe(false);
  });

  it('clears a previous error when reopened', () => {
    const { component, api } = setup();
    api.failWith = 409;

    component.sourceKind.set('documents');
    component.handleSubmit(new Event('submit'));
    expect(component.addError()).not.toBe('');

    component.onStateChanged('open');

    expect(component.addError()).toBe('');
  });
});
