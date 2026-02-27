import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

@Component({
  selector: 'app-party-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports],
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (party of parties(); track party.id) {
        <button
          type="button"
          (click)="partySelect.emit(party.id)"
          class="w-full cursor-pointer rounded-lg border border-border p-4 text-left transition-colors hover:bg-muted"
        >
          <p class="font-medium text-foreground">{{ party.name }}</p>
          @if (party.description) {
            <p class="mt-1 text-sm text-muted-foreground line-clamp-2">{{ party.description }}</p>
          }
          <div class="mt-2 text-xs text-muted-foreground">
            {{ party.documentCount }} {{ +party.documentCount === 1 ? 'document' : 'documents' }}
          </div>
        </button>
      }
    </div>

    <!-- Desktop table layout -->
    <div class="hidden overflow-hidden rounded-md border md:block">
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>Name</th>
              <th hlmTh>Description</th>
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
                <td hlmTd class="text-muted-foreground">
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
