import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-commodity-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Commodity actions"
      (click)="$event.stopPropagation()"
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          @if (!commodity().isSystemManaged) {
            <button hlmDropdownMenuItem (triggered)="edit.emit(commodity())">
              <ng-icon hlm name="lucidePencil" size="sm" />
              Edit
            </button>
          }
          @if (commodity().canDelete) {
            <hlm-dropdown-menu-separator />
            <button
              hlmDropdownMenuItem
              variant="destructive"
              (triggered)="delete.emit(commodity())"
            >
              <ng-icon hlm name="lucideTrash2" size="sm" />
              Delete
            </button>
          }
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class CommodityRowActionsComponent {
  commodity = input.required<CommodityResponse>();
  edit = output<CommodityResponse>();
  delete = output<CommodityResponse>();
}
