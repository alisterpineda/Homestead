import { Component, ChangeDetectionStrategy } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSettings } from '@ng-icons/lucide';
import { HlmIconImports } from '@/components/ui/icon';
import { HlmEmptyImports } from '@/components/ui/empty';

@Component({
  selector: 'app-admin-index',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmEmptyImports],
  providers: [provideIcons({ lucideSettings })],
  template: `
    <div>
      <!-- Page Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-bold tracking-tight text-foreground">Admin</h1>
        <p class="text-sm text-muted-foreground">System administration and settings</p>
      </div>

      <section hlmEmpty>
        <hlm-empty-media variant="icon">
          <ng-icon hlm name="lucideSettings" size="xl" />
        </hlm-empty-media>
        <div hlmEmptyHeader>
          <h3 hlmEmptyTitle>Admin features coming soon</h3>
          <p hlmEmptyDescription>Configuration and system management tools will be available here.</p>
        </div>
      </section>
    </div>
  `,
})
export class AdminIndexComponent {}
