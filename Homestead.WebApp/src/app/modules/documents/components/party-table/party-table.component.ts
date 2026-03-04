import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

@Component({
  selector: 'app-party-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, HlmCheckboxImports],
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
                  aria-label="Select all parties"
                />
              </th>
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
                <td hlmTd class="w-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
                  <hlm-checkbox
                    [checked]="isSelected(party.id)"
                    (checkedChange)="toggleSelection(party.id, $event)"
                    [aria-label]="'Select ' + party.name"
                  />
                </td>
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
  selectedIds = input<Set<string>>(new Set());
  selectionChange = output<Set<string>>();
  partySelect = output<string>();

  allSelected = computed(() => {
    const parties = this.parties();
    const selected = this.selectedIds();
    return parties.length > 0 && parties.every((p) => selected.has(p.id));
  });

  indeterminate = computed(() => {
    const parties = this.parties();
    const selected = this.selectedIds();
    const count = parties.filter((p) => selected.has(p.id)).length;
    return count > 0 && count < parties.length;
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
      const next = new Set(this.parties().map((p) => p.id));
      this.selectionChange.emit(next);
    } else {
      this.selectionChange.emit(new Set());
    }
  }
}
