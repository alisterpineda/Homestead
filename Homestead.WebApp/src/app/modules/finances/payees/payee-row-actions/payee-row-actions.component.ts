import { Component, ChangeDetectionStrategy, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideMerge, lucideTrash2 } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

@Component({
  selector: 'app-payee-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideMerge, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Payee actions"
      (click)="onTriggerClick($event)"
    >
      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(payee())">
            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
            Edit
          </button>
          <button hlmDropdownMenuItem (triggered)="merge.emit(payee())">
            <ng-icon name="lucideMerge" class="text-[length:--spacing(4)]" />
            Merge into…
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            [disabled]="deleteBlocked()"
            [title]="deleteTitle()"
            (triggered)="delete.emit(payee())"
          >
            <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class PayeeRowActionsComponent {
  payee = input.required<PayeeResponse>();
  edit = output<PayeeResponse>();
  merge = output<PayeeResponse>();
  delete = output<PayeeResponse>();

  /** In use by any transaction blocks delete — archive is the retire path for an in-use payee. */
  protected deleteBlocked = computed(() => this.payee().transactionCount > 0);

  protected deleteTitle = computed<string | null>(() => {
    const count = this.payee().transactionCount;
    return count > 0
      ? `In use by ${count} transaction${count === 1 ? '' : 's'} — archive instead`
      : null;
  });

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}
