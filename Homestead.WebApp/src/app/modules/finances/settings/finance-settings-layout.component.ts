import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-finance-settings-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      <header>
        <h1 class="text-2xl font-semibold">Finance settings</h1>
      </header>

      <nav class="flex gap-1 border-b border-border">
        <a
          routerLink="general"
          routerLinkActive="border-primary text-foreground"
          [routerLinkActiveOptions]="{ exact: false }"
          class="-mb-px inline-flex items-center border-b-2 border-transparent px-3 py-2 text-sm text-muted-foreground hover:text-foreground"
        >General</a>
        <a
          routerLink="price-sources"
          routerLinkActive="border-primary text-foreground"
          [routerLinkActiveOptions]="{ exact: false }"
          class="-mb-px inline-flex items-center border-b-2 border-transparent px-3 py-2 text-sm text-muted-foreground hover:text-foreground"
        >Price sources</a>
      </nav>

      <div class="flex min-h-0 flex-1 flex-col">
        <router-outlet />
      </div>
    </div>
  `,
})
export class FinanceSettingsLayoutComponent {}
