import { Routes } from '@angular/router';
import { InventoryLayoutComponent } from './layout/inventory-layout.component';
import { InventoryBrowsePageComponent } from './browse/inventory-browse-page.component';
import { InventoryListPageComponent } from './list/inventory-list-page.component';
import { AreaListPageComponent } from './areas/area-list-page.component';
import { TagListComponent } from './tags/tag-list.component';
import { BrandListComponent } from './brands/brand-list.component';
import { ItemDetailPageComponent } from './items/item-detail-page/item-detail-page.component';

export const INVENTORY_ROUTES: Routes = [
  {
    path: '',
    component: InventoryLayoutComponent,
    children: [
      // Overview is folded into Browse (a collapsible warning region); the root now lands there.
      { path: '', redirectTo: 'browse', pathMatch: 'full' },
      // Browse-by-place: one route per drilled node, all sharing InventoryBrowsePageComponent so
      // deeper drills into the same kind reuse the instance. The URL names only the current node;
      // the ancestor trail comes from the API's PlaceContentsResponse.path.
      { path: 'browse', component: InventoryBrowsePageComponent, data: { title: 'Browse' } },
      { path: 'browse/area/:areaId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'area' } },
      // Containers are a first-class Browse node (Explore mode): drilling area→container→sub-container
      // stays on this surface with a continuous breadcrumb. Stepping into the container's record is
      // the opt-in "Details" button, which routes out to /inventory/items/:id (Detail mode).
      { path: 'browse/container/:containerId', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'container' } },
      { path: 'browse/unplaced', component: InventoryBrowsePageComponent, data: { title: 'Browse', placeKind: 'unplaced' } },
      { path: 'items', component: InventoryListPageComponent, data: { title: 'Items' } },
      // Durables and consumables are the same Item row; a single dispatcher loads the record, learns its
      // kind, and renders the matching detail view. Replaces the old durables/:id + consumables/:id pair.
      { path: 'items/:id', component: ItemDetailPageComponent, data: { title: 'Item' } },
      { path: 'areas', component: AreaListPageComponent, data: { title: 'Areas' } },
      { path: 'tags', component: TagListComponent, data: { title: 'Tags' } },
      { path: 'brands', component: BrandListComponent, data: { title: 'Brands' } },
      // Consumables merged into the unified Items list; deep links keep working.
      { path: 'consumables', redirectTo: 'items', pathMatch: 'full' },
    ],
  },
];
