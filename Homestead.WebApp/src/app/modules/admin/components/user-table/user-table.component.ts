import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmBadge } from '@/shared/components/spartan/badge';
import type { UserResponse } from '@/api/generated/models/user-response';

@Component({
  selector: 'app-user-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, HlmTableImports, HlmCheckboxImports, HlmBadge],
  template: `
    <div class="overflow-clip rounded-md border">
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead class="sticky top-0 z-10 bg-background">
            <tr hlmTr>
              <th hlmTh class="w-10">
                <hlm-checkbox
                  [checked]="allSelected()"
                  [indeterminate]="indeterminate()"
                  (checkedChange)="toggleAll($event)"
                  aria-label="Select all users"
                />
              </th>
              <th hlmTh>Display Name</th>
              <th hlmTh>Email</th>
              <th hlmTh class="hidden md:table-cell">Role</th>
              <th hlmTh class="hidden md:table-cell">Status</th>
              <th hlmTh class="hidden md:table-cell">Created</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (user of users(); track user.id) {
              <tr
                hlmTr
                tabindex="0"
                (click)="userSelect.emit(user.id)"
                (keydown.enter)="userSelect.emit(user.id)"
                class="cursor-pointer"
              >
                <td hlmTd class="w-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
                  <hlm-checkbox
                    [checked]="isSelected(user.id)"
                    (checkedChange)="toggleSelection(user.id, $event)"
                    [aria-label]="'Select ' + user.displayName"
                  />
                </td>
                <td hlmTd class="font-medium">{{ user.displayName }}</td>
                <td hlmTd class="text-muted-foreground">{{ user.email ?? '\u2014' }}</td>
                <td hlmTd class="hidden md:table-cell">
                  @switch (user.role) {
                    @case ('Admin') {
                      <span hlmBadge variant="destructive">Admin</span>
                    }
                    @case ('Contributor') {
                      <span hlmBadge>Contributor</span>
                    }
                    @case ('Viewer') {
                      <span hlmBadge variant="secondary">Viewer</span>
                    }
                    @case ('Guest') {
                      <span hlmBadge variant="outline">Guest</span>
                    }
                  }
                </td>
                <td hlmTd class="hidden md:table-cell">
                  @if (user.isActive) {
                    <span hlmBadge variant="outline">Active</span>
                  } @else {
                    <span hlmBadge variant="secondary" class="opacity-60">Inactive</span>
                  }
                </td>
                <td hlmTd class="hidden text-muted-foreground md:table-cell">
                  {{ user.createdAt | date: 'mediumDate' }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class UserTableComponent {
  users = input.required<UserResponse[]>();
  selectedIds = input<Set<string>>(new Set());
  selectionChange = output<Set<string>>();
  userSelect = output<string>();

  allSelected = computed(() => {
    const users = this.users();
    const selected = this.selectedIds();
    return users.length > 0 && users.every((u) => selected.has(u.id));
  });

  indeterminate = computed(() => {
    const users = this.users();
    const selected = this.selectedIds();
    const count = users.filter((u) => selected.has(u.id)).length;
    return count > 0 && count < users.length;
  });

  isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  toggleSelection(id: string, checked: boolean): void {
    const next = new Set(this.selectedIds());
    if (checked) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.selectionChange.emit(next);
  }

  toggleAll(checked: boolean): void {
    if (checked) {
      const next = new Set(this.users().map((u) => u.id));
      this.selectionChange.emit(next);
    } else {
      this.selectionChange.emit(new Set());
    }
  }
}
