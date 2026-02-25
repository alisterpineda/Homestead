import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@/shared/components/spartan/sonner';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster],
  template: `
    <router-outlet />
    <hlm-toaster position="bottom-right" [richColors]="true" [closeButton]="true" />
  `,
})
export class App {}
