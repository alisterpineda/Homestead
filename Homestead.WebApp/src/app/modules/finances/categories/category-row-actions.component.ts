import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { CategoryResponse } from '@/api/generated/models/category-response';

@Component({
  selector: 'app-category-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Category actions"
      (click)="$event.stopPropagation()"
    >
      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(category())">
            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
            Edit
          </button>
          <hlm-dropdown-menu-separator />
          <button hlmDropdownMenuItem variant="destructive" (triggered)="delete.emit(category())">
            <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class CategoryRowActionsComponent {
  category = input.required<CategoryResponse>();
  edit = output<CategoryResponse>();
  delete = output<CategoryResponse>();
}
