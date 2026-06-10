import { Component, ChangeDetectionStrategy, inject, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronsUpDown,
  lucideLayoutGrid,
  lucideSettings,
  lucideFileText,
  lucideWallet,
  lucidePackage,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { modules } from '@/modules/registry';
import type { ModuleDefinition } from '@/shared/types/modules';
import { AuthService } from '@/shared/services/auth.service';

@Component({
  selector: 'app-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmSidebarImports, HlmDropdownMenuImports],
  providers: [
    provideIcons({ lucideChevronsUpDown, lucideLayoutGrid, lucideSettings, lucideFileText, lucideWallet, lucidePackage }),
  ],
  template: `
    <ul hlmSidebarMenu>
      <li hlmSidebarMenuItem>
        <button
          hlmSidebarMenuButton
          [hlmDropdownMenuTrigger]="menu"
          side="bottom"
          align="start"
          aria-label="Switch module"
        >
          <ng-icon hlm [name]="activeModule()?.icon ?? 'lucideLayoutGrid'" size="sm" />
          <span class="truncate font-semibold">{{ activeModule()?.label ?? 'Select Module' }}</span>
          <ng-icon hlm name="lucideChevronsUpDown" size="sm" class="ml-auto" />
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
      </li>
    </ul>
  `,
})
export class AppSwitcherComponent {
  private auth = inject(AuthService);

  readonly activeModule = input<ModuleDefinition | undefined>();

  protected visibleModules = computed(() =>
    modules.filter((m) => !m.requiredRole || this.auth.hasRole(m.requiredRole)),
  );
}
