import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { cn } from '@/shared/utils';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

@Component({
  selector: 'app-party-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (party of parties(); track party.id) {
        <div
          (click)="partySelect.emit(party.id)"
          [class]="
            cn(
              'cursor-pointer rounded-lg border border-border p-4 transition-colors',
              selectedPartyId() === party.id
                ? 'bg-accent-50'
                : 'hover:bg-surface-raised'
            )
          "
        >
          <p class="font-medium text-text-primary">{{ party.name }}</p>
          @if (party.description) {
            <p class="mt-1 text-sm text-text-secondary line-clamp-2">{{ party.description }}</p>
          }
          <div class="mt-2 text-xs text-text-muted">
            {{ party.documentCount }} {{ +party.documentCount === 1 ? 'document' : 'documents' }}
          </div>
        </div>
      }
    </div>

    <!-- Desktop table layout -->
    <div class="hidden overflow-hidden rounded-lg border border-border md:block">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-surface-raised">
            <th class="px-4 py-3 font-medium text-text-secondary">Name</th>
            <th class="px-4 py-3 font-medium text-text-secondary">Description</th>
            <th class="px-4 py-3 font-medium text-text-secondary text-right">Documents</th>
          </tr>
        </thead>
        <tbody>
          @for (party of parties(); track party.id) {
            <tr
              (click)="partySelect.emit(party.id)"
              [class]="
                cn(
                  'border-b border-border last:border-0 cursor-pointer transition-colors',
                  selectedPartyId() === party.id
                    ? 'bg-accent-50'
                    : 'hover:bg-surface-raised'
                )
              "
            >
              <td class="px-4 py-3 font-medium text-text-primary">{{ party.name }}</td>
              <td class="px-4 py-3 text-text-secondary">
                <span class="line-clamp-1">{{ party.description || '—' }}</span>
              </td>
              <td class="px-4 py-3 text-text-secondary text-right">{{ party.documentCount }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class PartyTableComponent {
  parties = input.required<GetPartiesResponse[]>();
  selectedPartyId = input<string | null>(null);
  partySelect = output<string>();

  cn = cn;
}
