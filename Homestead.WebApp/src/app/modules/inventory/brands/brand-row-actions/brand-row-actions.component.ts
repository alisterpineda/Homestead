import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideGitMerge, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { BrandResponse } from '@/api/generated/models/brand-response';

@Component({
  selector: 'app-brand-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideGitMerge, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Brand actions"
      (click)="onTriggerClick($event)"
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(brand())">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Rename
          </button>
          <button hlmDropdownMenuItem (triggered)="merge.emit(brand())">
            <ng-icon hlm name="lucideGitMerge" size="sm" />
            Merge into…
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            [disabled]="deleteBlocked()"
            [title]="deleteTitle()"
            (triggered)="delete.emit(brand())"
          >
            <ng-icon hlm name="lucideTrash2" size="sm" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class BrandRowActionsComponent {
  brand = input.required<BrandResponse>();
  edit = output<BrandResponse>();
  merge = output<BrandResponse>();
  delete = output<BrandResponse>();

  /** In use by any item blocks delete — merge is the retire path for an in-use brand. */
  protected deleteBlocked = computed(() => this.brand().itemCount > 0);

  protected deleteTitle = computed<string | null>(() => {
    const count = this.brand().itemCount;
    return count > 0
      ? `In use by ${count} item${count === 1 ? '' : 's'} — merge instead`
      : null;
  });

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}
