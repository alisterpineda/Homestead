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
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { isTerminalStatus } from '../shared/item-status-meta';
import type { InventoryRow } from './inventory-row';

@Component({
  selector: 'app-inventory-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
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
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(row())">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Edit
          </button>
          @if (row().durable?.durableKind === 'Container') {
            <button hlmDropdownMenuItem (triggered)="viewContents.emit(row())">
              <ng-icon hlm name="lucidePackageOpen" size="sm" />
              View contents
            </button>
          } @else if (row().durable?.durableKind === 'Assembly') {
            <button hlmDropdownMenuItem (triggered)="viewContents.emit(row())">
              <ng-icon hlm name="lucidePackageOpen" size="sm" />
              View components
            </button>
          }
          <button hlmDropdownMenuItem (triggered)="viewDetails.emit(row())">
            <ng-icon hlm name="lucideExternalLink" size="sm" />
            View details
          </button>
          <button hlmDropdownMenuItem (triggered)="archiveRestore.emit(row())">
            <ng-icon hlm [name]="terminal() ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
            {{ terminal() ? 'Restore' : 'Archive' }}
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            (triggered)="delete.emit(row())"
          >
            <ng-icon hlm name="lucideTrash2" size="sm" />
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
}
