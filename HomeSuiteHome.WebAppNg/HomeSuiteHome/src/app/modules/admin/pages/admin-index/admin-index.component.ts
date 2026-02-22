import { Component, ChangeDetectionStrategy } from '@angular/core';
import { Settings } from 'lucide-angular';
import { PageHeaderComponent } from '@/shared/components/ui/page-header/page-header.component';
import { EmptyStateComponent } from '@/shared/components/ui/empty-state/empty-state.component';

@Component({
  selector: 'app-admin-index',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeaderComponent, EmptyStateComponent],
  template: `
    <div>
      <app-page-header title="Admin" description="System administration and settings" />
      <app-empty-state
        [icon]="settingsIcon"
        title="Admin features coming soon"
        description="Configuration and system management tools will be available here."
      />
    </div>
  `,
})
export class AdminIndexComponent {
  settingsIcon = Settings;
}
