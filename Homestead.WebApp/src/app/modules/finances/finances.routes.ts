import { Routes } from '@angular/router';
import { FinancesLayoutComponent } from './layout/finances-layout.component';
import { AccountListPageComponent } from './accounts/account-list-page.component';
import { AccountDetailPageComponent } from './accounts/account-detail-page.component';
import { CommodityListPageComponent } from './commodities/commodity-list-page.component';
import { CommodityDetailPageComponent } from './commodities/commodity-detail-page.component';
import { ReportsPageComponent } from './reports/reports-page.component';
import { FinanceSettingsPageComponent } from './settings/finance-settings-page.component';

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
      { path: 'settings', component: FinanceSettingsPageComponent, data: { title: 'Settings' } },
    ],
  },
];
