import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { SidebarNavItemComponent } from '../sidebar-nav-item/sidebar-nav-item.component';
import type { SidebarItem } from '@/shared/types/modules';

@Component({
  selector: 'app-sidebar-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SidebarNavItemComponent],
  template: `
    <nav class="flex flex-col gap-1 px-3">
      @for (item of items(); track item.path) {
        <app-sidebar-nav-item [item]="item" />
      }
    </nav>
  `,
})
export class SidebarNavComponent {
  items = input.required<SidebarItem[]>();
}
