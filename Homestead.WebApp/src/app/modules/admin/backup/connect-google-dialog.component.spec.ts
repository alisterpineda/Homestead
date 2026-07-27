import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { ConnectGoogleDialogComponent } from './connect-google-dialog.component';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import type { ConnectBackupTargetResponse } from '@/api/generated/models/connect-backup-target-response';
import type { PollBackupTargetConnectionResponse } from '@/api/generated/models/poll-backup-target-connection-response';

class FakeApi {
  connectResponse: ConnectBackupTargetResponse = {
    userCode: 'ABCD-EFGH',
    verificationUrl: 'https://www.google.com/device',
    expiresAt: '2026-07-01T12:30:00Z',
    intervalSeconds: 5,
    pollToken: 'sealed-token',
  };

  /** Replies to hand back, in order; the last one repeats. */
  pollResponses: PollBackupTargetConnectionResponse[] = [{ status: 'Pending' }];

  connectFails = false;
  pollFails = false;

  /** When set, polls hang on this until the test emits — so a reply can land mid-flight. */
  deferredPoll: Subject<PollBackupTargetConnectionResponse> | null = null;

  connectCalls = 0;
  pollCalls: string[] = [];
  confirmCalls: string[] = [];

  connect() {
    this.connectCalls++;
    if (this.connectFails) return throwError(() => new Error('nope'));
    return of(this.connectResponse);
  }

  pollConnection(_key: string, pollToken: string) {
    this.pollCalls.push(pollToken);
    if (this.pollFails) return throwError(() => new Error('nope'));
    if (this.deferredPoll) return this.deferredPoll.asObservable();

    const next = this.pollResponses.length > 1 ? this.pollResponses.shift()! : this.pollResponses[0];
    return of(next);
  }

  confirmConnection(_key: string, pendingToken: string) {
    this.confirmCalls.push(pendingToken);
    return of({ targetKey: 'gdrive', connectedAccountEmail: 'new@example.test', resetCopyCount: 3 });
  }
}

/** Hosts the dialog so its required input is bound the way the card binds it. */
@Component({
  imports: [ConnectGoogleDialogComponent],
  template: `<app-connect-google-dialog targetKey="gdrive" (connected)="connectedCount = connectedCount + 1">
    Connect
  </app-connect-google-dialog>`,
})
class HostComponent {
  connectedCount = 0;
}

function setup() {
  const api = new FakeApi();

  TestBed.configureTestingModule({
    providers: [provideHttpClient(), { provide: AdminBackupApiService, useValue: api }],
  });

  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();

  const dialog = fixture.debugElement.children[0].componentInstance as ConnectGoogleDialogComponent;

  return { fixture, api, dialog, host: fixture.componentInstance };
}

describe('ConnectGoogleDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the user code and verification URL once the flow starts', () => {
    const { api, dialog } = setup();

    dialog.start();

    expect(api.connectCalls).toBe(1);
    expect(dialog.stage()).toBe('waiting');
    expect(dialog.userCode()).toBe('ABCD-EFGH');
    expect(dialog.verificationUrl()).toBe('https://www.google.com/device');
  });

  it('surfaces a failed start rather than spinning forever', () => {
    const { api, dialog } = setup();
    api.connectFails = true;

    dialog.start();

    expect(dialog.stage()).toBe('error');
    expect(dialog.errorMessage()).toBeTruthy();
  });

  it('keeps waiting while Google reports the admin has not consented', () => {
    const { dialog } = setup();
    dialog.start();

    dialog.poll();

    expect(dialog.stage()).toBe('waiting');
  });

  // Google asks for a slower cadence and keeps asking until we comply, so the backoff has to persist
  // across rounds rather than delaying only the next one.
  it('backs off cumulatively on SlowDown', () => {
    const { api, dialog } = setup();
    api.pollResponses = [{ status: 'SlowDown' }, { status: 'SlowDown' }, { status: 'Pending' }];

    dialog.start();
    dialog.poll();
    dialog.poll();

    expect(dialog['intervalSeconds']).toBe(15);
  });

  it('emits connected and closes on a granted authorization', () => {
    const { api, dialog, host } = setup();
    api.pollResponses = [{ status: 'Connected', connectedAccountEmail: 'owner@example.test' }];

    dialog.start();
    dialog.poll();

    expect(host.connectedCount).toBe(1);
  });

  it('reports an expired code with a retry affordance', () => {
    const { api, dialog } = setup();
    api.pollResponses = [{ status: 'Expired' }];

    dialog.start();
    dialog.poll();

    expect(dialog.stage()).toBe('expired');
  });

  it('reports a declined request', () => {
    const { api, dialog } = setup();
    api.pollResponses = [{ status: 'Denied' }];

    dialog.start();
    dialog.poll();

    expect(dialog.stage()).toBe('denied');
  });

  // Switching accounts abandons the existing mirror, so it is never implicit.
  it('stops at a confirmation step when a different account consented', () => {
    const { api, dialog, host } = setup();
    api.pollResponses = [
      {
        status: 'AccountMismatch',
        connectedAccountEmail: 'new@example.test',
        existingAccountEmail: 'old@example.test',
        pendingToken: 'sealed-pending',
      },
    ];

    dialog.start();
    dialog.poll();

    expect(dialog.stage()).toBe('mismatch');
    expect(dialog.pendingEmail()).toBe('new@example.test');
    expect(dialog.existingEmail()).toBe('old@example.test');

    // Nothing was accepted yet.
    expect(host.connectedCount).toBe(0);
    expect(api.confirmCalls).toEqual([]);
  });

  it('confirms the switch with the sealed pending token', () => {
    const { api, dialog, host } = setup();
    api.pollResponses = [
      {
        status: 'AccountMismatch',
        connectedAccountEmail: 'new@example.test',
        existingAccountEmail: 'old@example.test',
        pendingToken: 'sealed-pending',
      },
    ];

    dialog.start();
    dialog.poll();
    dialog.confirmSwitch({ close: () => {} });

    expect(api.confirmCalls).toEqual(['sealed-pending']);
    expect(host.connectedCount).toBe(1);
  });

  it('reports a lost connection while waiting', () => {
    const { api, dialog } = setup();
    dialog.start();
    api.pollFails = true;

    dialog.poll();

    expect(dialog.stage()).toBe('error');
  });

  // A reply still in flight when the dialog closes must not drive a stage transition on a flow nobody
  // is watching any more.
  it('ignores a poll reply that lands after the dialog closed', () => {
    const { api, dialog } = setup();
    dialog.start();

    api.deferredPoll = new Subject<PollBackupTargetConnectionResponse>();
    dialog.poll();

    dialog.onStateChanged('closed');
    api.deferredPoll.next({ status: 'Expired' });

    expect(dialog.stage()).toBe('waiting');
  });

  it('ignores a poll reply that lands after the component is destroyed', () => {
    const { fixture, api, dialog } = setup();
    dialog.start();

    api.deferredPoll = new Subject<PollBackupTargetConnectionResponse>();
    dialog.poll();

    fixture.destroy();
    api.deferredPoll.next({ status: 'Denied' });

    expect(dialog.stage()).toBe('waiting');
  });

  // Reopening must not show the previous attempt's terminal state.
  it('restarts cleanly when reopened', () => {
    const { api, dialog } = setup();
    api.pollResponses = [{ status: 'Denied' }];

    dialog.start();
    dialog.poll();
    expect(dialog.stage()).toBe('denied');

    api.pollResponses = [{ status: 'Pending' }];
    dialog.onStateChanged('open');

    expect(dialog.stage()).toBe('waiting');
    expect(api.connectCalls).toBe(2);
  });
});
