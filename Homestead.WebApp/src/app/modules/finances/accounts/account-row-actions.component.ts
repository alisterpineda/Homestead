import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import type { AccountResponse } from '@/api/generated/models/account-response';

@Component({
  selector: 'app-account-row-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucidePencil, lucideArchive, lucideArchiveRestore, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Account actions"
      (click)="$event.stopPropagation()"
    >
      <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <hlm-dropdown-menu-group>
          <button hlmDropdownMenuItem (triggered)="edit.emit(account())">
            <ng-icon hlm name="lucidePencil" size="sm" />
            Edit
          </button>
          <button hlmDropdownMenuItem (triggered)="archiveToggle.emit(account())">
            <ng-icon hlm [name]="account().isArchived ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
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
            <ng-icon hlm name="lucideTrash2" size="sm" />
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
}
