import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  viewChild,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { AdminBackupApiService } from '@/api/services/admin-backup-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BackupSourceKindResponse } from '@/api/generated/models/backup-source-kind-response';

/**
 * Pairs a source kind with this card's target. The target is implied by where the dialog was opened
 * from, so the only choice is what to back up — and the options come from the catalog the server sent
 * with the list, never a hard-coded list here.
 */
@Component({
  selector: 'app-add-backup-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmDialogImports, HlmButton, HlmLabel, HlmSelectImports],
  providers: [provideIcons({ lucidePlus })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button
        hlmBtn
        variant="outline"
        size="sm"
        hlmDialogTrigger
        [disabled]="available().length === 0"
        [attr.data-testid]="'add-backup-' + targetKey()"
      >
        <ng-icon hlm name="lucidePlus" size="sm" />
        Add backup
      </button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Add backup</h2>
          <p hlmDialogDescription>
            Mirror a dataset to <span class="font-mono">{{ targetKey() }}</span
            >.
          </p>
        </div>

        <form (submit)="handleSubmit($event)" class="flex flex-col gap-4">
          <div class="flex flex-col gap-2">
            <label hlmLabel>What to back up</label>
            <!--
              itemToString is what the closed trigger renders. Without it the trigger falls back to the
              raw value — the persisted kind key, "documents" — because the options live behind
              *hlmSelectPortal and are not instantiated until the listbox opens, so there is no option
              for the trigger to read a label off. Static-option selects elsewhere never hit this.
            -->
            <hlm-select
              [value]="sourceKind()"
              (valueChange)="sourceKind.set($any($event))"
              [itemToString]="kindToString"
            >
              <hlm-select-trigger class="w-full" data-testid="source-kind-trigger">
                <hlm-select-value placeholder="Choose a dataset" />
              </hlm-select-trigger>
              <hlm-select-content *hlmSelectPortal>
                <hlm-select-group>
                  @for (kind of available(); track kind.key) {
                    <hlm-select-item [value]="kind.key">{{ kind.displayName }}</hlm-select-item>
                  }
                </hlm-select-group>
              </hlm-select-content>
            </hlm-select>
          </div>

          @if (addError()) {
            <p class="text-sm text-destructive" data-testid="add-backup-error">{{ addError() }}</p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button hlmBtn type="submit" [disabled]="!canSubmit() || adding()">
              {{ adding() ? 'Adding...' : 'Add' }}
            </button>
          </div>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class AddBackupDialogComponent {
  targetKey = input.required<string>();

  /** The whole catalog, as sent with the list. */
  sourceKinds = input.required<BackupSourceKindResponse[]>();

  /** Kinds already paired with this target — one pairing per kind per target is the invariant. */
  existingKinds = input<string[]>([]);

  added = output();

  private api = inject(AdminBackupApiService);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);
  private dialog = viewChild(BrnDialog);

  sourceKind = signal('');
  adding = signal(false);
  addError = signal('');

  /**
   * Filtering the picker is the friendly half of the duplicate rule; the server's 409 (backed by a
   * unique index) is the enforcing half, surfaced inline below.
   */
  available = computed(() => {
    const taken = new Set(this.existingKinds().map(k => k.toLowerCase()));
    return this.sourceKinds().filter(k => !taken.has(k.key.toLowerCase()));
  });

  canSubmit = computed(() => this.sourceKind().length > 0);

  /** Falls back to the raw key so an unlabelled kind is still nameable rather than blank. */
  kindToString = (key: string): string =>
    this.sourceKinds().find(k => k.key === key)?.displayName ?? key;

  onStateChanged(state: 'open' | 'closed'): void {
    if (state !== 'open') return;

    // Pre-select when there is only one thing left to add — the common case with a single kind
    // registered, where making the admin open a select of one is pure friction.
    const options = this.available();
    this.sourceKind.set(options.length === 1 ? options[0].key : '');
    this.adding.set(false);
    this.addError.set('');
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;

    this.adding.set(true);
    this.addError.set('');

    this.api
      .createSource({ sourceKind: this.sourceKind(), targetKey: this.targetKey() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.adding.set(false);
          toast.success('Backup added.');
          this.dialog()?.close();
          this.added.emit();
        },
        error: (err: unknown) => {
          this.adding.set(false);
          this.addError.set(this.errorHandler.getErrorMessage(err, 'Failed to add the backup.'));
        },
      });
  }
}
