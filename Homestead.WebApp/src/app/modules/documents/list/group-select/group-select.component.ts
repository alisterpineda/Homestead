import { Component, ChangeDetectionStrategy, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { HlmSelectImports } from '@/shared/components/spartan/select';

import { DocumentApiService } from '@/api/services/document-api.service';
import type { GetMyGroupsResponse } from '@/api/generated/models/get-my-groups-response';

@Component({
  selector: 'app-group-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports, ],
  template: `
    <hlm-select [value]="value()" (valueChange)="onValueChange($any($event))" [itemToString]="groupToString">
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

  groups = toSignal(
    this.documentApi.getMyGroups().pipe(catchError(() => of<GetMyGroupsResponse[]>([]))),
    { initialValue: [] as GetMyGroupsResponse[] },
  );
  groupToString = (id: string): string => {
    if (id === '') return 'All Groups';
    return this.groups().find(g => g.id === id)?.name ?? id;
  };

  onValueChange(value: string): void {
    this.valueChange.emit(value);
  }
}
