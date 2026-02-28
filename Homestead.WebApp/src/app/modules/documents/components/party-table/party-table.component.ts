import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

@Component({
  selector: 'app-party-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports],
  template: `
    <div class="overflow-hidden rounded-md border">
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>Name</th>
              <th hlmTh class="hidden md:table-cell">Description</th>
              <th hlmTh class="text-right">Documents</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (party of parties(); track party.id) {
              <tr
                hlmTr
                tabindex="0"
                (click)="partySelect.emit(party.id)"
                (keydown.enter)="partySelect.emit(party.id)"
                class="cursor-pointer"
              >
                <td hlmTd class="font-medium">{{ party.name }}</td>
                <td hlmTd class="hidden text-muted-foreground md:table-cell">
                  <span class="line-clamp-1">{{ party.description || '\u2014' }}</span>
                </td>
                <td hlmTd class="text-right text-muted-foreground">{{ party.documentCount }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class PartyTableComponent {
  parties = input.required<GetPartiesResponse[]>();
  partySelect = output<string>();
}
