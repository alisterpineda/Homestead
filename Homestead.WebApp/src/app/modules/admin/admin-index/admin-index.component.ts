import { Component, ChangeDetectionStrategy } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSettings } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';

@Component({
  selector: 'app-admin-index',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmEmptyImports],
  providers: [provideIcons({ lucideSettings })],
  template: `
    <div>
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon">
            <ng-icon name="lucideSettings" />
          </div>
          <div hlmEmptyTitle>Admin features coming soon</div>
          <div hlmEmptyDescription>Configuration and system management tools will be available here.</div>
        </div>
      </div>
    </div>
  `,
})
export class AdminIndexComponent {}
