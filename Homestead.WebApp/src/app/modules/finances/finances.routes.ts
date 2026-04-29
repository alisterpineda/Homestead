import { Routes } from '@angular/router';
import { FinancesLayoutComponent } from './layout/finances-layout.component';
import { AccountListPageComponent } from './accounts/account-list-page.component';
import { AccountDetailPageComponent } from './accounts/account-detail-page.component';
import { CommodityListPageComponent } from './commodities/commodity-list-page.component';
import { CommodityDetailPageComponent } from './commodities/commodity-detail-page.component';
import { ReportsPageComponent } from './reports/reports-page.component';
import { FinanceSettingsLayoutComponent } from './settings/finance-settings-layout.component';
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
      { path: 'commodities', component: CommodityListPageComponent, data: { title: 'Commodities' } },
      { path: 'commodities/:id', component: CommodityDetailPageComponent, data: { title: 'Commodity' } },
      { path: 'reports', component: ReportsPageComponent, data: { title: 'Reports' } },
      {
        path: 'settings',
        component: FinanceSettingsLayoutComponent,
        data: { title: 'Settings' },
        children: [
          { path: '', redirectTo: 'general', pathMatch: 'full' },
          { path: 'general', component: GeneralSettingsComponent, data: { title: 'Finance settings' } },
          { path: 'price-sources', component: PriceSourceListComponent, data: { title: 'Price sources' } },
          { path: 'price-sources/:source', component: PriceSourceDetailComponent, data: { title: 'Price source' } },
        ],
      },
    ],
  },
];
