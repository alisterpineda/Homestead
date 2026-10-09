import {
  Component,
  ChangeDetectionStrategy,
  OnDestroy,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucideExternalLink, lucideTriangleAlert } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';

/** Where the dialog is in the device-authorization flow. */
export type ConnectStage = 'starting' | 'waiting' | 'mismatch' | 'expired' | 'denied' | 'error';

@Component({
  selector: 'app-connect-google-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmDialogImports, HlmButton, HlmSpinner],
  providers: [provideIcons({ lucideCopy, lucideExternalLink, lucideTriangleAlert })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button
        hlmBtn
        [variant]="triggerVariant()"
        size="sm"
        hlmDialogTrigger
        [attr.data-testid]="'connect-google-' + targetKey()"
      >
        <ng-content />
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Connect a Google account</h2>
          <p hlmDialogDescription>
            Mirror <span class="font-mono">{{ targetKey() }}</span> into a Google Drive you own.
          </p>
        </div>

        @switch (stage()) {
          @case ('starting') {
            <div class="flex justify-center py-10"><hlm-spinner /></div>
          }

          @case ('waiting') {
            <div class="flex flex-col gap-4" data-testid="connect-waiting">
              <ol class="flex list-decimal flex-col gap-3 pl-5 text-sm">
                <li>
                  Open
                  <a
                    class="inline-flex items-center gap-1 font-medium underline"
                    [href]="verificationUrl()"
                    target="_blank"
                    rel="noopener noreferrer"
                    data-testid="connect-verification-url"
                  >
                    {{ verificationUrl() }}
                    <ng-icon name="lucideExternalLink" class="text-[length:--spacing(3)]" />
                  </a>
                  on any device.
                </li>
                <li>
                  Enter this code:
                  <div class="mt-2 flex items-center gap-2">
                    <code
                      class="rounded-md border bg-muted px-3 py-2 font-mono text-xl tracking-widest"
                      data-testid="connect-user-code"
                      >{{ userCode() }}</code
                    >
                    <button
                      hlmBtn
                      variant="outline"
                      size="icon-sm"
                      type="button"
                      aria-label="Copy code"
                      data-testid="connect-copy-code"
                      (click)="copyCode()"
                    >
                      <ng-icon name="lucideCopy" class="text-[length:--spacing(4)]" />
                    </button>
                  </div>
                </li>
                <li>Approve the request. This dialog updates on its own.</li>
              </ol>

              <p class="text-xs text-muted-foreground">
                Homestead can only see and manage files it creates — never the rest of your Drive.
              </p>

              <div class="flex items-center gap-2 text-sm text-muted-foreground">
                <hlm-spinner class="size-4" />
                Waiting for approval&hellip;
              </div>
            </div>
          }

          @case ('mismatch') {
            <div class="flex flex-col gap-4" data-testid="connect-mismatch">
              <p class="flex items-start gap-2 text-sm text-destructive">
                <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(4)] mt-0.5 shrink-0" />
                <span>
                  You approved <span class="font-medium">{{ pendingEmail() }}</span
                  >, but this target is connected to
                  <span class="font-medium">{{ existingEmail() }}</span
                  >.
                </span>
              </p>
              <p class="text-sm text-muted-foreground">
                Switching accounts resets what Homestead believes it has mirrored: file ids from the old
                account mean nothing in the new one, so the next run re-uploads everything. The existing
                mirror is left where it is, in the old account, for you to delete by hand.
              </p>
            </div>
          }

          @case ('expired') {
            <p class="py-4 text-sm text-muted-foreground" data-testid="connect-expired">
              The code expired before it was approved.
            </p>
          }

          @case ('denied') {
            <p class="py-4 text-sm text-destructive" data-testid="connect-denied">
              The request was declined in Google.
            </p>
          }

          @case ('error') {
            <p class="py-4 text-sm text-destructive" data-testid="connect-error">{{ errorMessage() }}</p>
          }
        }

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" type="button" hlmDialogClose>
            {{ stage() === 'mismatch' ? 'Cancel' : 'Close' }}
          </button>

          @switch (stage()) {
            @case ('mismatch') {
              <button
                hlmBtn
                variant="destructive"
                type="button"
                [disabled]="confirming()"
                data-testid="connect-confirm-switch"
                (click)="confirmSwitch(ctx)"
              >
                {{ confirming() ? 'Switching…' : 'Switch account and reset' }}
              </button>
            }
            @case ('expired') {
              <button hlmBtn type="button" data-testid="connect-retry" (click)="start()">Try again</button>
            }
            @case ('denied') {
              <button hlmBtn type="button" data-testid="connect-retry" (click)="start()">Try again</button>
            }
            @case ('error') {
              <button hlmBtn type="button" data-testid="connect-retry" (click)="start()">Try again</button>
            }
          }
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ConnectGoogleDialogComponent implements OnDestroy {
  targetKey = input.required<string>();

  /** Destructive when the card is showing a dead authorization — the button is a "fix this" affordance. */
  triggerVariant = input<'default' | 'outline' | 'destructive'>('outline');

  connected = output();

  private api = inject(AdminBackupApiService);
  private errorHandler = inject(ErrorHandlerService);
  private dialog = viewChild(BrnDialog);

  stage = signal<ConnectStage>('starting');
  userCode = signal('');
  verificationUrl = signal('');
  errorMessage = signal('');
  pendingEmail = signal<string | null>(null);
  existingEmail = signal<string | null>(null);
  confirming = signal(false);

  private pollToken = '';
  private pendingToken = '';
  private intervalSeconds = 5;
  private timer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Bumped on every open/close and every restart. The poll loop compares it before acting, so a reply
   * that arrives after the dialog closed (or after the admin hit "Try again") is dropped instead of
   * driving a stage transition on a flow nobody is watching any more.
   */
  private generation = 0;

  ngOnDestroy(): void {
    this.cancelPolling();
  }

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.start();
      return;
    }

    this.cancelPolling();
  }

  start(): void {
    this.cancelPolling();

    const generation = ++this.generation;

    this.stage.set('starting');
    this.userCode.set('');
    this.verificationUrl.set('');
    this.errorMessage.set('');
    this.pendingEmail.set(null);
    this.existingEmail.set(null);
    this.confirming.set(false);
    this.pendingToken = '';

    this.api.connect(this.targetKey()).subscribe({
      next: response => {
        if (generation !== this.generation) return;

        this.pollToken = response.pollToken;
        this.intervalSeconds = Math.max(1, response.intervalSeconds);
        this.userCode.set(response.userCode);
        this.verificationUrl.set(response.verificationUrl);
        this.stage.set('waiting');
        this.scheduleNextPoll();
      },
      error: (err: unknown) => {
        if (generation !== this.generation) return;

        this.stage.set('error');
        this.errorMessage.set(
          this.errorHandler.getErrorMessage(err, 'Could not start the Google connection.'),
        );
      },
    });
  }

  /**
   * One poll round. Exposed rather than buried in the timer so tests can step the flow deterministically
   * instead of leaning on fake timers.
   */
  poll(): void {
    const generation = this.generation;

    this.api.pollConnection(this.targetKey(), this.pollToken).subscribe({
      next: response => {
        if (generation !== this.generation) return;

        switch (response.status) {
          case 'Pending':
            this.scheduleNextPoll();
            return;

          case 'SlowDown':
            // Google asks for a slower cadence and keeps asking until we comply, so the backoff has to
            // persist across rounds rather than delaying just the next one.
            this.intervalSeconds += 5;
            this.scheduleNextPoll();
            return;

          case 'Connected':
            this.cancelPolling();
            toast.success(
              response.connectedAccountEmail
                ? `Connected as ${response.connectedAccountEmail}.`
                : 'Connected to Google.',
            );
            this.dialog()?.close();
            this.connected.emit();
            return;

          case 'AccountMismatch':
            this.cancelPolling();
            this.pendingToken = response.pendingToken ?? '';
            this.pendingEmail.set(response.connectedAccountEmail ?? null);
            this.existingEmail.set(response.existingAccountEmail ?? null);
            this.stage.set('mismatch');
            return;

          case 'Expired':
            this.cancelPolling();
            this.stage.set('expired');
            return;

          case 'Denied':
            this.cancelPolling();
            this.stage.set('denied');
            return;
        }
      },
      error: (err: unknown) => {
        if (generation !== this.generation) return;

        this.cancelPolling();
        this.stage.set('error');
        this.errorMessage.set(
          this.errorHandler.getErrorMessage(err, 'Lost contact while waiting for approval.'),
        );
      },
    });
  }

  confirmSwitch(ctx: { close: () => void }): void {
    if (!this.pendingToken) return;

    this.confirming.set(true);

    this.api.confirmConnection(this.targetKey(), this.pendingToken).subscribe({
      next: response => {
        this.confirming.set(false);
        toast.success(
          `Connected as ${response.connectedAccountEmail ?? 'the new account'}. ` +
            `${response.resetCopyCount} recorded cop${response.resetCopyCount === 1 ? 'y' : 'ies'} ` +
            'were forgotten; the next run re-uploads everything.',
        );
        ctx.close();
        this.connected.emit();
      },
      error: (err: unknown) => {
        this.confirming.set(false);
        this.stage.set('error');
        this.errorMessage.set(
          this.errorHandler.getErrorMessage(err, 'Could not switch the connected account.'),
        );
      },
    });
  }

  copyCode(): void {
    navigator.clipboard?.writeText(this.userCode()).then(
      () => toast.success('Code copied.'),
      () => toast.error('Could not copy the code.'),
    );
  }

  private scheduleNextPoll(): void {
    this.cancelTimer();
    this.timer = setTimeout(() => this.poll(), this.intervalSeconds * 1000);
  }

  private cancelPolling(): void {
    this.generation++;
    this.cancelTimer();
  }

  private cancelTimer(): void {
    if (this.timer === null) return;

    clearTimeout(this.timer);
    this.timer = null;
  }
}
