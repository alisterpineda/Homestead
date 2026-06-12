import { Routes } from '@angular/router';
import { InventoryLayoutComponent } from './layout/inventory-layout.component';
import { InventoryListPageComponent } from './list/inventory-list-page.component';
import { AreaListPageComponent } from './areas/area-list-page.component';
import { SupplyDetailPageComponent } from './supplies/supply-detail-page.component';

export const INVENTORY_ROUTES: Routes = [
  {
    path: '',
    component: InventoryLayoutComponent,
    children: [
      { path: '', redirectTo: 'items', pathMatch: 'full' },
      { path: 'items', component: InventoryListPageComponent, data: { title: 'Items' } },
      { path: 'areas', component: AreaListPageComponent, data: { title: 'Areas' } },
      // Supplies merged into the unified Items list; deep links keep working.
      { path: 'supplies', redirectTo: 'items', pathMatch: 'full' },
      { path: 'supplies/:id', component: SupplyDetailPageComponent, data: { title: 'Supply' } },
    ],
  },
];
