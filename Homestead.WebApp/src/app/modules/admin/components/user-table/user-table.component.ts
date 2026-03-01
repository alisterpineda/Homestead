import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HlmTableImports } from '@/shared/components/spartan/table';
import type { GetUsersResponse } from '@/api/generated/models/get-users-response';

@Component({
  selector: 'app-user-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, HlmTableImports],
  template: `
    <div class="overflow-hidden rounded-md border">
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>Display Name</th>
              <th hlmTh>Email</th>
              <th hlmTh class="hidden md:table-cell">Created</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (user of users(); track user.id) {
              <tr hlmTr>
                <td hlmTd class="font-medium">{{ user.displayName }}</td>
                <td hlmTd class="text-muted-foreground">{{ user.email ?? '\u2014' }}</td>
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
  users = input.required<GetUsersResponse[]>();
}
