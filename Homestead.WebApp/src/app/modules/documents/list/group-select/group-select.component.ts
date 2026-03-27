import { Component, ChangeDetectionStrategy, inject, signal, input, output } from '@angular/core';
import { HlmSelectImports } from '@/shared/components/spartan/select';

import { DocumentApiService } from '@/api/services/document-api.service';
import type { GetMyGroupsResponse } from '@/api/generated/models/get-my-groups-response';

@Component({
  selector: 'app-group-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports, ],
  template: `
    <hlm-select [value]="value()" (valueChange)="onValueChange($any($event))">
      <hlm-select-trigger [class]="triggerClass()">
        <hlm-select-value [placeholder]="placeholder()" />
      </hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal>
<hlm-select-group>
        <hlm-select-item value="">All Groups</hlm-select-item>
        @for (group of groups(); track group.id) {
          <hlm-select-item [value]="group.id">{{ group.name }}</hlm-select-item>
        }
      </hlm-select-group>
</hlm-select-content>
    </hlm-select>
  `,
})
export class GroupSelectComponent {
  value = input<string>('');
  placeholder = input<string>('Filter by group');
  triggerClass = input<string>('w-48');
  valueChange = output<string>();

  private documentApi = inject(DocumentApiService);

  groups = signal<GetMyGroupsResponse[]>([]);

  constructor() {
    this.documentApi.getMyGroups().subscribe({
      next: (r) => {
        this.groups.set(r as GetMyGroupsResponse[]);
      },
      error: () => {
        this.groups.set([]);
      },
    });
  }

  onValueChange(value: string): void {
    this.valueChange.emit(value);
  }
}
