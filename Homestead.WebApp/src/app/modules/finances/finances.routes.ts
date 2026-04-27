import { Routes } from '@angular/router';
import { FinancesLayoutComponent } from './layout/finances-layout.component';
import { AccountListPageComponent } from './accounts/account-list-page.component';
import { AccountDetailPageComponent } from './accounts/account-detail-page.component';
import { FinanceSettingsPageComponent } from './settings/finance-settings-page.component';

export const FINANCES_ROUTES: Routes = [
  {
    path: '',
    component: FinancesLayoutComponent,
    children: [
      { path: '', redirectTo: 'accounts', pathMatch: 'full' },
      { path: 'accounts', component: AccountListPageComponent, data: { title: 'Accounts' } },
      { path: 'accounts/:id', component: AccountDetailPageComponent, data: { title: 'Account' } },
      { path: 'settings', component: FinanceSettingsPageComponent, data: { title: 'Finance settings' } },
    ],
  },
];
