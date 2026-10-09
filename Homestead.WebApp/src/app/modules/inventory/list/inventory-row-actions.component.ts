import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideEllipsisVertical,
  lucidePencil,
  lucideArchive,
  lucideArchiveRestore,
  lucideTrash2,
  lucidePackageOpen,
  lucideExternalLink,
} from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { isTerminalStatus } from '../shared/item-status-meta';
import type { InventoryRow } from './inventory-row';

@Component({
  selector: 'app-inventory-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({
    lucideEllipsisVertical,
    lucidePencil,
    lucideArchive,
    lucideArchiveRestore,
    lucideTrash2,
    lucidePackageOpen,
    lucideExternalLink,
  })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      [aria-label]="row().kind === 'consumable' ? 'Consumable actions' : 'Durable actions'"
      (click)="onTriggerClick($event)"
      (keydown.enter)="onTriggerKeydown($event)"
      (keydown.space)="onTriggerKeydown($event)"
    >
      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(row())">
            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
            Edit
          </button>
          @if (row().durable?.isContainer) {
            <button hlmDropdownMenuItem (triggered)="viewContents.emit(row())">
              <ng-icon name="lucidePackageOpen" class="text-[length:--spacing(4)]" />
              View contents
            </button>
          } @else if (row().durable?.hasComponents) {
            <button hlmDropdownMenuItem (triggered)="viewContents.emit(row())">
              <ng-icon name="lucidePackageOpen" class="text-[length:--spacing(4)]" />
              View components
            </button>
          }
          <button hlmDropdownMenuItem (triggered)="viewDetails.emit(row())">
            <ng-icon name="lucideExternalLink" class="text-[length:--spacing(4)]" />
            View details
          </button>
          <button hlmDropdownMenuItem (triggered)="archiveRestore.emit(row())">
            <ng-icon [name]="terminal() ? 'lucideArchiveRestore' : 'lucideArchive'" class="text-[length:--spacing(4)]" />
            {{ terminal() ? 'Restore' : 'Archive' }}
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            (triggered)="delete.emit(row())"
          >
            <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class InventoryRowActionsComponent {
  row = input.required<InventoryRow>();
  edit = output<InventoryRow>();
  viewContents = output<InventoryRow>();
  viewDetails = output<InventoryRow>();
  archiveRestore = output<InventoryRow>();
  delete = output<InventoryRow>();

  protected terminal = computed(() => isTerminalStatus(this.row().status));

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }

  /**
   * Keyboard twin of the click guard: the host row/card opens the quick panel on Enter/Space, so an
   * activation keystroke on the trigger must not bubble up to it. Only propagation is stopped — the
   * default is left alone, because CdkMenuTrigger relies on the button's native click (synthesised
   * from this keystroke) to actually open the menu.
   */
  protected onTriggerKeydown(event: Event): void {
    event.stopPropagation();
  }
}
