import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { AreaResponse } from '@/api/generated/models/area-response';

@Component({
  selector: 'app-area-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Area actions"
      (click)="onTriggerClick($event)"
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(area())">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Rename
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            [disabled]="deleteBlocked()"
            [title]="deleteTitle()"
            (triggered)="delete.emit(area())"
          >
            <ng-icon hlm name="lucideTrash2" size="sm" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class AreaRowActionsComponent {
  area = input.required<AreaResponse>();
  /** True when the area has sub-areas — they must be removed before it can be deleted. */
  hasChildren = input(false);
  edit = output<AreaResponse>();
  delete = output<AreaResponse>();

  protected deleteBlocked = computed(() => this.area().liveItemCount > 0 || this.hasChildren());

  protected deleteTitle = computed<string | null>(() => {
    if (this.hasChildren())
      return 'Areas with sub-areas cannot be deleted — move or delete the sub-areas first.';
    if (this.area().liveItemCount > 0)
      return 'Areas with items in them cannot be deleted — move or retire the items first.';
    return null;
  });

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}
