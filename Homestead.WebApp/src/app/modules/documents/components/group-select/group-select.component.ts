import { Component, ChangeDetectionStrategy, inject, signal, input, output } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getMyGroups } from '@/api/generated/fn/documents/get-my-groups';
import type { GetMyGroupsResponse } from '@/api/generated/models/get-my-groups-response';

@Component({
  selector: 'app-group-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports, BrnSelectImports],
  template: `
    <brn-select [value]="value()" (valueChange)="onValueChange($any($event))" [placeholder]="placeholder()">
      <hlm-select-trigger [class]="triggerClass()">
        <hlm-select-value />
      </hlm-select-trigger>
      <hlm-select-content>
        <hlm-option value="">All Groups</hlm-option>
        @for (group of groups(); track group.id) {
          <hlm-option [value]="group.id">{{ group.name }}</hlm-option>
        }
      </hlm-select-content>
    </brn-select>
  `,
})
export class GroupSelectComponent {
  value = input<string>('');
  placeholder = input<string>('Filter by group');
  triggerClass = input<string>('w-48');
  valueChange = output<string>();

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);

  groups = signal<GetMyGroupsResponse[]>([]);

  constructor() {
    getMyGroups(this.http, this.apiConfig.rootUrl).subscribe({
      next: (r) => {
        this.groups.set(r.body as GetMyGroupsResponse[]);
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
