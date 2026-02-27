import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

@Component({
  selector: 'app-tag-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports],
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (tag of tags(); track tag.id) {
        <button
          type="button"
          (click)="tagSelect.emit(tag.id)"
          class="w-full cursor-pointer rounded-lg border border-border p-4 text-left transition-colors hover:bg-muted"
        >
          <div class="flex items-center gap-2">
            @if (tag.color) {
              <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="tag.color"></span>
            }
            <p class="font-medium text-foreground">{{ tag.name }}</p>
          </div>
          <p class="mt-1 text-sm text-muted-foreground">{{ tag.path }}</p>
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
                <td hlmTd class="font-medium">
                  <span class="flex items-center gap-2">
                    @if (tag.color) {
                      <span class="h-3 w-3 shrink-0 rounded-full" [style.background-color]="tag.color"></span>
                    }
                    {{ tag.name }}
                  </span>
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
  tagSelect = output<string>();
}
