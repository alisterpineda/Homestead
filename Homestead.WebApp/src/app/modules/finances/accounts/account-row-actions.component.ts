import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2 } from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { AccountResponse } from '@/api/generated/models/account-response';

@Component({
  selector: 'app-account-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Account actions"
      (click)="onTriggerClick($event)"
    >
      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(account())">
            <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
            Edit
          </button>
          <button hlmDropdownMenuItem (triggered)="archiveToggle.emit(account())">
            <ng-icon [name]="account().isArchived ? 'lucideArchiveRestore' : 'lucideArchive'" class="text-[length:--spacing(4)]" />
            {{ account().isArchived ? 'Unarchive' : 'Archive' }}
          </button>
          <hlm-dropdown-menu-separator />
          <button
            hlmDropdownMenuItem
            variant="destructive"
            [disabled]="account().hasTransactions"
            [title]="account().hasTransactions ? 'Accounts with transactions cannot be deleted — archive them instead.' : null"
            (triggered)="delete.emit(account())"
          >
            <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
            Delete
          </button>
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class AccountRowActionsComponent {
  account = input.required<AccountResponse>();
  edit = output<AccountResponse>();
  archiveToggle = output<AccountResponse>();
  delete = output<AccountResponse>();

  protected onTriggerClick(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();
  }
}
