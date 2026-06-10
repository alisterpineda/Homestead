import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2, lucidePackageOpen } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { isTerminalStatus } from '../shared/item-status-meta';
import type { ItemResponse } from '@/api/generated/models/item-response';

@Component({
  selector: 'app-item-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2, lucidePackageOpen })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Item actions"
      (click)="onTriggerClick($event)"
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(item())">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Edit
          </button>
          @if (item().isContainer) {
            <button hlmDropdownMenuItem (triggered)="viewContents.emit(item())">
              <ng-icon hlm name="lucidePackageOpen" size="sm" />
              View contents
            </button>
          }
          <button hlmDropdownMenuItem (triggered)="archiveRestore.emit(item())">
            <ng-icon hlm [name]="terminal() ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
            {{ terminal() ? 'Restore' : 'Archive' }}
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            (triggered)="delete.emit(item())"
          >
            <ng-icon hlm name="lucideTrash2" size="sm" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class ItemRowActionsComponent {
  item = input.required<ItemResponse>();
  edit = output<ItemResponse>();
  viewContents = output<ItemResponse>();
  archiveRestore = output<ItemResponse>();
  delete = output<ItemResponse>();

  protected terminal = computed(() => isTerminalStatus(this.item().status));

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}
