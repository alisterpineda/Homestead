import { Routes } from '@angular/router';
import { InventoryLayoutComponent } from './layout/inventory-layout.component';
import { ItemListPageComponent } from './items/item-list-page.component';
import { AreaListPageComponent } from './areas/area-list-page.component';

export const INVENTORY_ROUTES: Routes = [
  {
    path: '',
    component: InventoryLayoutComponent,
    children: [
      { path: '', redirectTo: 'items', pathMatch: 'full' },
      { path: 'items', component: ItemListPageComponent, data: { title: 'Items' } },
      { path: 'areas', component: AreaListPageComponent, data: { title: 'Areas' } },
    ],
  },
];
