import { Routes } from '@angular/router';
import { adminGuard } from '@/shared/guards/admin.guard';
import { FinancesLayoutComponent } from './layout/finances-layout.component';
import { AccountListPageComponent } from './accounts/account-list-page.component';
import { AccountDetailPageComponent } from './accounts/account-detail-page.component';
import { StatementImportReviewPageComponent } from './accounts/statement-import-review-page.component';
import { CommodityListPageComponent } from './commodities/commodity-list-page.component';
import { CommodityDetailPageComponent } from './commodities/commodity-detail-page.component';
import { CategoryListPageComponent } from './categories/category-list-page.component';
import { PayeeListComponent } from './payees/payee-list.component';
import { ReportsPageComponent } from './reports/reports-page.component';
import { GeneralSettingsComponent } from './settings/general/general-settings.component';
import { PriceSourceListComponent } from './settings/price-sources/price-source-list.component';
import { PriceSourceDetailComponent } from './settings/price-sources/price-source-detail.component';

export const FINANCES_ROUTES: Routes = [
  {
    path: '',
    component: FinancesLayoutComponent,
    children: [
      { path: '', redirectTo: 'accounts', pathMatch: 'full' },
      { path: 'accounts', component: AccountListPageComponent, data: { title: 'Accounts' } },
      { path: 'accounts/:id', component: AccountDetailPageComponent, data: { title: 'Account' } },
      {
        path: 'accounts/:id/statement-import/:jobId',
        component: StatementImportReviewPageComponent,
        data: { title: 'Import statement' },
      },
      { path: 'commodities', component: CommodityListPageComponent, data: { title: 'Commodities' } },
      { path: 'commodities/:id', component: CommodityDetailPageComponent, data: { title: 'Commodity' } },
      { path: 'categories', component: CategoryListPageComponent, data: { title: 'Categories' } },
      { path: 'payees', component: PayeeListComponent, data: { title: 'Payees' } },
      { path: 'reports', component: ReportsPageComponent, data: { title: 'Reports' } },
      { path: 'settings', redirectTo: 'settings/general', pathMatch: 'full' },
      { path: 'settings/general', component: GeneralSettingsComponent, data: { title: 'General' } },
      { path: 'settings/price-sources', component: PriceSourceListComponent, canActivate: [adminGuard], data: { title: 'Price sources' } },
      { path: 'settings/price-sources/:source', component: PriceSourceDetailComponent, canActivate: [adminGuard], data: { title: 'Price source' } },
    ],
  },
];
