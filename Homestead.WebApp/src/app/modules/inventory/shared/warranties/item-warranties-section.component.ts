import {
  Component,
  ChangeDetectionStrategy,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
  DestroyRef,
  ElementRef,
  LOCALE_ID,
} from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideEllipsisVertical,
  lucidePencil,
  lucideShieldCheck,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import {
  warrantyBadge,
  warrantyExpiryText,
  warrantyProviderLabel,
} from './warranty-status-meta';
import {
  ItemWarrantyComposerComponent,
  type WarrantyPayload,
  type WarrantySeed,
} from './item-warranty-composer.component';
import type { ItemWarrantyResponse } from '@/api/generated/models/item-warranty-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

/**
 * The Warranties section of the durable detail page: the composer at the head, then one row per
 * recorded warranty in the server's order (soonest expiry first, open-ended last).
 *
 * A household durable routinely carries more than one — a manufacturer warranty plus a retailer's
 * extended plan, or a full warranty plus a longer one on the motor — so this is a list, not a field
 * pair on the edit form. Rows are deliberately terse: who stands behind it, the tier note when there
 * is one, and when it runs out. Only *expiring* and *expired* earn a pill; an in-force warranty is
 * the unremarkable case.
 *
 * Reads through one keyed fetch and, like the activity feed, **every mutation reloads rather than
 * splicing** — the row `status` is server-derived, so a spliced-in row would carry a status this
 * client had to guess. The `toSignal(toObservable(key) → switchMap)` shape keeps the current rows on
 * screen until the replacement lands, so a reload never flashes a spinner.
 */
@Component({
  selector: 'app-item-warranties-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmButton,
    HlmSpinner,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ItemWarrantyComposerComponent,
  ],
  providers: [provideIcons({
    lucideEllipsisVertical,
    lucidePencil,
    lucideShieldCheck,
    lucideTrash2,
  })],
  template: `
    <section class="space-y-2">
      <h2 class="text-sm font-medium text-muted-foreground">Warranties</h2>

      <div class="rounded-md border">
        <div class="border-b px-4 py-3">
          <app-item-warranty-composer
            #headerComposer
            [acquiredOn]="acquiredOn()"
            [disabled]="editSeed() !== null"
            [busy]="submitting()"
            (submitted)="onCreate($event)"
          />
        </div>

        @if (loadError()) {
          <p class="px-4 py-3 text-sm text-muted-foreground">
            Couldn't load warranties.
            <button type="button" class="underline underline-offset-4 hover:text-foreground" (click)="retry()">Retry</button>
          </p>
        } @else if (!loaded()) {
          <div class="flex justify-center py-6"><hlm-spinner /></div>
        } @else if (rows().length === 0) {
          <p class="px-4 py-3 text-sm text-muted-foreground">No warranties recorded.</p>
        } @else {
          <ul class="divide-y">
            @for (row of rows(); track row.id) {
              @let editing = editSeed();
              <li>
                @if (editing && editing.id === row.id) {
                  <div class="px-4 py-3" #editRow>
                    <app-item-warranty-composer
                      mode="edit"
                      [initial]="editing.seed"
                      [acquiredOn]="acquiredOn()"
                      [busy]="saving()"
                      (submitted)="onSaveEdit(row, $event)"
                      (cancelled)="cancelEdit()"
                    />
                  </div>
                } @else {
                  <div class="flex items-center gap-2.5 px-4 py-2.5">
                    <div class="flex size-6 shrink-0 items-center justify-center rounded-full bg-muted">
                      <ng-icon class="text-[length:--spacing(3)]" name="lucideShieldCheck" />
                    </div>
                    <div class="min-w-0 flex-1">
                      <div class="flex flex-wrap items-baseline gap-x-2">
                        <span
                          class="text-sm font-medium"
                          [class.text-muted-foreground]="row.status === 'Expired'"
                        >{{ providerLabel(row) }}</span>
                        @if (row.notes) {
                          <span class="text-sm text-muted-foreground">&middot; {{ row.notes }}</span>
                        }
                        @if (badge(row); as pill) {
                          <span [class]="pill.badgeClass">{{ pill.label }}</span>
                        }
                      </div>
                      <p class="text-xs text-muted-foreground">{{ expiryText(row) }}</p>
                    </div>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-xs"
                      class="ml-auto shrink-0 self-start"
                      [hlmDropdownMenuTrigger]="rowMenu"
                      aria-label="Warranty actions"
                    >
                      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
                    </button>
                    <ng-template #rowMenu>
                      <hlm-dropdown-menu class="w-36">
                        <hlm-dropdown-menu-group>
                          <button hlmDropdownMenuItem (triggered)="startEdit(row)">
                            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
                            Edit
                          </button>
                          <button hlmDropdownMenuItem variant="destructive" (triggered)="onDeleteRequest(row)">
                            <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
                            Delete
                          </button>
                        </hlm-dropdown-menu-group>
                      </hlm-dropdown-menu>
                    </ng-template>
                  </div>
                }
              </li>
            }
          </ul>
        }
      </div>
    </section>

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete this warranty?</h2>
          <p hlmAlertDialogDescription>This permanently removes the warranty. There is no undo.</p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" [disabled]="deleting()" (click)="confirmDelete(ctx)">
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ItemWarrantiesSectionComponent {
  private api = inject(InventoryItemApiService);
  private errorHandler = inject(ErrorHandlerService);
  private injector = inject(Injector);
  private destroyRef = inject(DestroyRef);
  private locale = inject(LOCALE_ID);

  itemId = input.required<string>();
  /** The item's brand, so a null provider can resolve to "Bosch · manufacturer" at display time. */
  brandName = input<string | null>(null);
  /** The item's acquired-on date — the base the composer's year presets count from. */
  acquiredOn = input<string | null>(null);
  /** The host page's refresh tick, so an edit-sheet save re-reads the derived statuses too. */
  refreshTick = input(0);
  /**
   * Fires after every successful create/update/delete so the host can refresh what it owns — the
   * activity feed (which projects these rows) and the durable's own `warrantyStatus`. The list
   * itself is reloaded locally; the host's tick folds into the same fetch key, so this costs no
   * extra list fetch.
   */
  changed = output<void>();

  // --- The list -------------------------------------------------------------------------------
  private localTick = signal(0);
  protected loadError = signal(false);

  private fetchKey = computed(() => ({
    id: this.itemId(),
    tick: this.refreshTick() + this.localTick(),
  }));

  private fetched = toSignal(
    toObservable(this.fetchKey).pipe(
      switchMap(({ id }) => {
        this.loadError.set(false);
        if (!id) return of(null);
        return this.api.getItemWarranties({ id }).pipe(
          catchError(() => {
            this.loadError.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  protected rows = computed<ItemWarrantyResponse[]>(() => this.fetched() ?? []);
  /** Distinguishes the first in-flight fetch from a resolved-but-empty list (no empty-state flash). */
  protected loaded = computed(() => this.fetched() !== null);

  // --- Row state ------------------------------------------------------------------------------
  /** The row being edited plus its seed, captured once so a reload underneath can't re-seed. */
  protected editSeed = seededLinkedSignal<string, { id: string; seed: WarrantySeed } | null>(
    this.itemId,
    () => null,
  );
  protected submitting = signal(false);
  protected saving = signal(false);

  protected pendingDelete = signal<ItemWarrantyResponse | null>(null);
  protected deleteState = seededLinkedSignal<string, BrnDialogState>(this.itemId, () => 'closed');
  protected deleting = signal(false);
  protected deleteError = signal('');

  private headerComposer = viewChild<ItemWarrantyComposerComponent>('headerComposer');
  private editRow = viewChild<ElementRef<HTMLElement>>('editRow');

  // --- Display helpers ------------------------------------------------------------------------
  protected providerLabel(row: ItemWarrantyResponse): string {
    return warrantyProviderLabel(row.provider, this.brandName());
  }

  protected expiryText(row: ItemWarrantyResponse): string {
    return warrantyExpiryText(row.expiresOn, row.status, this.locale);
  }

  protected badge(row: ItemWarrantyResponse) {
    return warrantyBadge(row.status);
  }

  // --- Handlers -------------------------------------------------------------------------------
  protected retry(): void {
    this.loadError.set(false);
    this.reload();
  }

  protected onCreate(payload: WarrantyPayload): void {
    this.submitting.set(true);
    this.api
      .createItemWarranty({ id: this.itemId(), body: payload })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        handleMutationError(this.errorHandler, this.submitting, 'Failed to add warranty.'),
      )
      .subscribe(() => {
        this.submitting.set(false);
        // Clears the expiry chip too — unlike the note date, a carried-over expiry writes bad data.
        this.headerComposer()?.clearText();
        this.reload();
        this.changed.emit();
      });
  }

  protected startEdit(row: ItemWarrantyResponse): void {
    if (this.submitting()) return;
    this.editSeed.set({
      id: row.id,
      seed: {
        provider: row.provider ?? null,
        expiresOn: row.expiresOn ?? null,
        notes: row.notes ?? null,
      },
    });
    afterNextRender(() => this.editRow()?.nativeElement.scrollIntoView({ block: 'nearest' }), {
      injector: this.injector,
    });
  }

  protected cancelEdit(): void {
    this.editSeed.set(null);
  }

  protected onSaveEdit(row: ItemWarrantyResponse, payload: WarrantyPayload): void {
    this.saving.set(true);
    this.api
      .updateItemWarranty({ id: this.itemId(), warrantyId: row.id, body: payload })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        handleMutationError(this.errorHandler, this.saving, 'Failed to save warranty.'),
      )
      .subscribe(() => {
        this.saving.set(false);
        this.editSeed.set(null);
        this.reload();
        this.changed.emit();
      });
  }

  protected onDeleteRequest(row: ItemWarrantyResponse): void {
    this.pendingDelete.set(row);
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  protected confirmDelete(ctx: { close: () => void }): void {
    const row = this.pendingDelete();
    if (!row) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.api
      .deleteItemWarranty({ id: this.itemId(), warrantyId: row.id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          ctx.close();
          this.pendingDelete.set(null);
          this.reload();
          this.changed.emit();
          toast.success('Warranty deleted.');
        },
        // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection
        // reason is what the user needs to read right here.
        error: (err: unknown) => {
          this.deleting.set(false);
          this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete warranty.'));
        },
      });
  }

  /** Every mutation re-reads rather than splicing — row `status` is derived server-side. */
  private reload(): void {
    this.localTick.update((v) => v + 1);
  }
}
