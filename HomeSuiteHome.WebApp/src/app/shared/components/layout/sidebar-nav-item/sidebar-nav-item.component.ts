import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import type { SidebarItem } from '@/shared/types/modules';

@Component({
  selector: 'app-sidebar-nav-item',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, LucideAngularModule],
  template: `
    <a
      [routerLink]="item().path"
      routerLinkActive="bg-sidebar-accent text-sidebar-accent-foreground"
      [routerLinkActiveOptions]="{ exact: true }"
      class="flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground md:py-2"
    >
      @if (item().icon) {
        <lucide-icon [img]="item().icon!" [size]="16" />
      }
      {{ item().label }}
    </a>
  `,
})
export class SidebarNavItemComponent {
  item = input.required<SidebarItem>();
}
