import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { NgTemplateOutlet } from '@angular/common';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { DurableDetailPageComponent } from '../../durables/durable-detail-page/durable-detail-page.component';
import { ConsumableDetailPageComponent } from '../../consumables/consumable-detail-page.component';
import type { ItemDetailResponse } from '@/api/generated/models/item-detail-response';

/**
 * Thin dispatcher for `/inventory/items/:id`. A durable and a consumable are the same `Item` row
 * underneath, discriminated by {@link ItemDetailResponse.fungibility}; this loads the unified detail in
 * one round-trip, owns the loading / not-found states centrally, then renders the kind-specific child
 * — preloaded with the matching payload so the child renders without a second fetch. The children
 * share this component's `ActivatedRoute`, so they still read the same `:id` for their secondary loads.
 */
@Component({
  selector: 'app-item-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    DurableDetailPageComponent,
    ConsumableDetailPageComponent,
  ],
  providers: [provideIcons({ lucideCircleAlert })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #notFoundState>
      <div class="flex min-h-0 flex-1 flex-col gap-6 p-4 md:p-6">
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Item not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      </div>
    </ng-template>

    @if (item(); as it) {
      @if (it.fungibility === 'NonFungible' && it.durable) {
        <app-durable-detail-page [preloaded]="it.durable" />
      } @else if (it.fungibility === 'Fungible' && it.consumable) {
        <app-consumable-detail-page [preloaded]="it.consumable" />
      } @else {
        <!-- Defensive: the item resolved but its kind/payload is unrenderable (contract drift) —
             show feedback rather than a blank surface. -->
        <ng-container [ngTemplateOutlet]="notFoundState" />
      }
    } @else if (notFound()) {
      <ng-container [ngTemplateOutlet]="notFoundState" />
    } @else {
      <div class="flex justify-center py-16"><hlm-spinner /></div>
    }
  `,
})
export class ItemDetailPageComponent {
  private itemApi = inject(InventoryItemApiService);
  private route = inject(ActivatedRoute);

  private itemId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  // The spinner shows whenever `item()` is null and we're not in the not-found state, so a separate
  // loading flag would be write-only — `notFound` is the only error state the template reads.
  notFound = signal(false);

  private fetchedItem = toSignal<ItemDetailResponse | null>(
    toObservable(this.itemId).pipe(
      switchMap((id) => {
        if (!id) return of(null);
        this.notFound.set(false);
        return this.itemApi.getItem({ id }).pipe(
          catchError(() => {
            this.notFound.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  // Only surface the item once it matches the current route id. A navigation therefore drops to the
  // central spinner (the previous item no longer matches) and remounts the child fresh with the
  // correct preload — never briefly rendering the prior item or the wrong kind's child.
  protected item = computed<ItemDetailResponse | null>(() => {
    const it = this.fetchedItem();
    return it && it.id === this.itemId() ? it : null;
  });
}
