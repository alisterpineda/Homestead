import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSettings } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';

@Component({
  selector: 'app-admin-index',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmEmptyImports, HlmButton],
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
        <div hlmEmptyContent>
          <a hlmBtn variant="outline" routerLink="/admin/settings">
            <ng-icon hlm name="lucideSettings" size="sm" class="mr-1" />
            System Settings
          </a>
        </div>
      </div>
    </div>
  `,
})
export class AdminIndexComponent {}
