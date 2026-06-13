import { Routes } from '@angular/router';
import { InventoryLayoutComponent } from './layout/inventory-layout.component';
import { InventoryOverviewPageComponent } from './overview/inventory-overview-page.component';
import { InventoryBrowsePageComponent } from './browse/inventory-browse-page.component';
import { InventoryListPageComponent } from './list/inventory-list-page.component';
import { AreaListPageComponent } from './areas/area-list-page.component';
import { SupplyDetailPageComponent } from './supplies/supply-detail-page.component';

export const INVENTORY_ROUTES: Routes = [
  {
    path: '',
    component: InventoryLayoutComponent,
    children: [
      { path: '', component: InventoryOverviewPageComponent, pathMatch: 'full', data: { title: 'Overview' } },
      // Browse-by-place: one route per drilled node, all sharing InventoryBrowsePageComponent so
      // deeper drills into the same kind reuse the instance. The URL names only the current node;
      // the ancestor trail comes from the API's PlaceContentsResponse.path.
      { path: 'browse', component: InventoryBrowsePageComponent, data: { title: 'Browse' } },
      { path: 'browse/area/:areaId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'area' } },
      { path: 'browse/item/:itemId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'container' } },
      { path: 'browse/unplaced', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'unplaced' } },
      { path: 'items', component: InventoryListPageComponent, data: { title: 'Items' } },
      { path: 'areas', component: AreaListPageComponent, data: { title: 'Areas' } },
      // Supplies merged into the unified Items list; deep links keep working.
      { path: 'supplies', redirectTo: 'items', pathMatch: 'full' },
      { path: 'supplies/:id', component: SupplyDetailPageComponent, data: { title: 'Supply' } },
    ],
  },
];
