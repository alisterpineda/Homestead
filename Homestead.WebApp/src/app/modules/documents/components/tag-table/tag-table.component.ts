import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

@Component({
  selector: 'app-tag-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, HlmCheckboxImports],
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (tag of tags(); track tag.id) {
        <div
          class="w-full rounded-lg border border-border p-4 transition-colors hover:bg-muted"
        >
          <div class="flex items-start gap-3">
            <hlm-checkbox
              [checked]="isSelected(tag.id)"
              (checkedChange)="toggleSelection(tag.id, $event)"
              [aria-label]="'Select ' + tag.name"
              class="mt-0.5"
            />
            <button
              type="button"
              (click)="tagSelect.emit(tag.id)"
              class="min-w-0 flex-1 cursor-pointer text-left"
            >
              <div class="flex items-center gap-2">
                @if (tag.color) {
                  <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="tag.color"></span>
                }
                <p class="font-medium text-foreground">{{ tag.name }}</p>
              </div>
              <p class="mt-1 text-sm text-muted-foreground">{{ tag.path }}</p>
            </button>
          </div>
        </div>
      }
    </div>

    <!-- Desktop table layout -->
    <div class="hidden overflow-hidden rounded-md border md:block">
      <div hlmTableContainer>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh class="w-10">
                <hlm-checkbox
                  [checked]="allSelected()"
                  [indeterminate]="indeterminate()"
                  (checkedChange)="toggleAll($event)"
                  aria-label="Select all tags"
                />
              </th>
              <th hlmTh>Name</th>
              <th hlmTh class="w-8"></th>
              <th hlmTh>Path</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (tag of tags(); track tag.id) {
              <tr
                hlmTr
                tabindex="0"
                (click)="tagSelect.emit(tag.id)"
                (keydown.enter)="tagSelect.emit(tag.id)"
                class="cursor-pointer"
              >
                <td hlmTd class="w-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
                  <hlm-checkbox
                    [checked]="isSelected(tag.id)"
                    (checkedChange)="toggleSelection(tag.id, $event)"
                    [aria-label]="'Select ' + tag.name"
                  />
                </td>
                <td hlmTd class="font-medium">{{ tag.name }}</td>
                <td hlmTd class="w-8">
                  @if (tag.color) {
                    <span class="h-3 w-3 rounded-full inline-block" [style.background-color]="tag.color"></span>
                  }
                </td>
                <td hlmTd class="text-muted-foreground">{{ tag.path }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class TagTableComponent {
  tags = input.required<GetTagsResponse[]>();
  selectedIds = input<Set<string>>(new Set());
  selectionChange = output<Set<string>>();
  tagSelect = output<string>();

  allSelected = computed(() => {
    const tags = this.tags();
    const selected = this.selectedIds();
    return tags.length > 0 && tags.every((t) => selected.has(t.id));
  });

  indeterminate = computed(() => {
    const tags = this.tags();
    const selected = this.selectedIds();
    const count = tags.filter((t) => selected.has(t.id)).length;
    return count > 0 && count < tags.length;
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
      const next = new Set(this.tags().map((t) => t.id));
      this.selectionChange.emit(next);
    } else {
      this.selectionChange.emit(new Set());
    }
  }
}
