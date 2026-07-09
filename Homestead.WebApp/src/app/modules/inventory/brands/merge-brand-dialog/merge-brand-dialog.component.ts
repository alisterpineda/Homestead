import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
  inject,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import { BrandPickerComponent, type BrandPickerValue } from '../../shared/brand-picker/brand-picker.component';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { openGeneration } from '@/shared/utils/open-generation';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

@Component({
  selector: 'app-merge-brand-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDialogImports, HlmButton, HlmLabel, BrandPickerComponent],
  template: `
    <hlm-dialog [state]="state()" (stateChanged)="onStateChanged($event)">
      <hlm-dialog-content *hlmDialogPortal class="sm:max-w-[440px]">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Merge brand</h2>
          <p hlmDialogDescription>
            Move every item from "{{ source()?.name }}" onto another brand, then delete "{{ source()?.name }}".
          </p>
        </div>

        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel>Merge into</label>
            <app-brand-picker
              [value]="target()"
              [allowCreate]="false"
              [excludeId]="source()?.id ?? null"
              [initialSearch]="initialTargetName()"
              placeholder="Select a brand to keep"
              ariaLabel="Target brand"
              (valueChange)="target.set($event)"
            />
          </div>

          @if (target(); as t) {
            <p class="text-sm text-muted-foreground">
              Move {{ source()?.itemCount }} item(s) from "{{ source()?.name }}" into "{{ t.name }}",
              then delete "{{ source()?.name }}". This can't be undone.
            </p>
          }

          <div hlmDialogFooter>
            <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
            <button
              hlmBtn
              variant="destructive"
              type="button"
              [disabled]="!canConfirm()"
              (click)="onConfirm()"
            >
              {{ merging() ? 'Merging...' : 'Merge' }}
            </button>
          </div>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MergeBrandDialogComponent {
  source = input<BrandResponse | null>(null);
  state = input<BrnDialogState>('closed');
  /** Optional pre-seed for the target search (rename-409 handoff supplies the attempted name). */
  initialTargetName = input('');
  merged = output<BrandResponse>();
  closed = output<void>();

  private openGen = openGeneration(this.state);
  private seedKey = computed(() => `${this.source()?.id ?? ''}|${this.state() === 'open'}|${this.openGen()}`);

  target = seededLinkedSignal<string, BrandPickerValue | null>(this.seedKey, () => null);
  merging = seededLinkedSignal(this.seedKey, () => false);

  private brandApi = inject(InventoryBrandApiService);
  private errorHandler = inject(ErrorHandlerService);

  canConfirm = computed(() => this.source() !== null && this.target() !== null && !this.merging());

  onConfirm(): void {
    const source = this.source();
    const target = this.target();
    if (!source || !target || this.merging()) return;

    this.merging.set(true);
    const result$ = this.brandApi.mergeBrand({ id: source.id, body: { targetId: target.id } });

    toast.promise(
      firstValueFrom(result$).then((updated) => {
        this.merging.set(false);
        this.merged.emit(updated);
        return `Merged into ${target.name}.`;
      }).catch((err) => {
        this.merging.set(false);
        throw err;
      }),
      {
        loading: 'Merging brands...',
        success: (message) => message as string,
        error: (err) => this.errorHandler.getErrorMessage(err, 'Failed to merge brands.'),
      },
    );
  }

  onStateChanged(state: BrnDialogState): void {
    if (state === 'closed') {
      this.closed.emit();
    }
  }
}
