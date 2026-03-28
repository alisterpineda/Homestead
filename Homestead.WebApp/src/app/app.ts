import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@/shared/components/spartan/sonner';
import { ThemeService } from '@/shared/services/theme.service';
import { AppUpdateService } from '@/shared/services/app-update.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster],
  template: `
    <router-outlet />
    <hlm-toaster position="bottom-right" />
  `,
})
export class App {
  constructor() {
    inject(ThemeService);
    inject(AppUpdateService);
  }
}
