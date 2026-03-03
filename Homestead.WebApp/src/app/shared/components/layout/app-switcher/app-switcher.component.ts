import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLayoutGrid, lucideSettings, lucideFileText } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmButton } from '@/shared/components/spartan/button';
import { modules } from '@/modules/registry';
import { AuthService } from '@/shared/services/auth.service';

@Component({
  selector: 'app-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmDropdownMenuImports, HlmButton],
  providers: [provideIcons({ lucideLayoutGrid, lucideSettings, lucideFileText })],
  template: `
    <button hlmBtn variant="ghost" size="icon-sm" [hlmDropdownMenuTrigger]="menu">
      <ng-icon hlm name="lucideLayoutGrid" size="sm" />
    </button>
    <ng-template #menu>
      <div hlmDropdownMenu class="w-52">
        @for (mod of visibleModules(); track mod.id) {
          <a [routerLink]="mod.basePath" hlmDropdownMenuItem class="gap-2">
            <ng-icon hlm [name]="mod.icon" size="sm" />
            {{ mod.label }}
          </a>
        }
      </div>
    </ng-template>
  `,
})
export class AppSwitcherComponent {
  private auth = inject(AuthService);

  visibleModules = computed(() => {
    return modules.filter((m) => !m.requiredRole || this.auth.hasRole(m.requiredRole));
  });
}
