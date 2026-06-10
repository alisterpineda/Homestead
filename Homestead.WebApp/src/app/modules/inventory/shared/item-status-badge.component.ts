import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import type { ItemStatus } from '@/api/generated/models/item-status';
import { ITEM_STATUS_META } from './item-status-meta';

@Component({
  selector: 'app-item-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span [class]="meta().badgeClass">{{ meta().label }}</span>`,
})
export class ItemStatusBadgeComponent {
  status = input.required<ItemStatus>();

  protected meta = computed(() => ITEM_STATUS_META[this.status()]);
}
