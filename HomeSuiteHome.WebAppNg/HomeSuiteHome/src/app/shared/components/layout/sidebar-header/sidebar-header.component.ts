import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { AppSwitcherComponent } from '../app-switcher/app-switcher.component';
import { ThemeToggleComponent } from '@/shared/components/theme/theme-toggle/theme-toggle.component';
import type { ModuleDefinition } from '@/shared/types/modules';

@Component({
  selector: 'app-sidebar-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule, AppSwitcherComponent, ThemeToggleComponent],
  template: `
    <div class="flex items-center gap-2 border-b border-border px-3 py-3">
      <app-switcher />
      @if (activeModule()) {
        <div class="flex items-center gap-2 overflow-hidden">
          <lucide-icon
            [img]="activeModule()!.icon"
            [size]="16"
            class="shrink-0 text-text-secondary"
          />
          <span class="truncate text-sm font-semibold text-text-primary">
            {{ activeModule()!.label }}
          </span>
        </div>
      }
      <div class="ml-auto">
        <app-theme-toggle />
      </div>
    </div>
  `,
})
export class SidebarHeaderComponent {
  activeModule = input<ModuleDefinition | undefined>();
}
