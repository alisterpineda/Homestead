import { Component, ChangeDetectionStrategy, input } from '@angular/core';

@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="flex flex-col gap-3 pb-4 sm:flex-row sm:items-center sm:justify-between sm:gap-0 sm:pb-6"
    >
      <div>
        <h1 class="text-2xl font-semibold text-foreground">{{ title() }}</h1>
        @if (description()) {
          <p class="mt-1 text-sm text-muted-foreground">{{ description() }}</p>
        }
      </div>
      <div>
        <ng-content select="[action]" />
      </div>
    </div>
  `,
})
export class PageHeaderComponent {
  title = input.required<string>();
  description = input('');
}
