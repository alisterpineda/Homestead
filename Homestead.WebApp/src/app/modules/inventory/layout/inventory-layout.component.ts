import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-inventory-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `<router-outlet />`,
})
export class InventoryLayoutComponent {}
