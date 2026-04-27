import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { FinanceSettingsService } from '../shared/finance-settings.service';

@Component({
  selector: 'app-finances-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `<router-outlet />`,
})
export class FinancesLayoutComponent {
  constructor() {
    inject(FinanceSettingsService).load();
  }
}
