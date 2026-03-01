import { Routes } from '@angular/router';
import { AdminIndexComponent } from './pages/admin-index/admin-index.component';
import { UserListComponent } from './pages/user-list/user-list.component';

export const ADMIN_ROUTES: Routes = [
  { path: '', component: AdminIndexComponent, data: { title: 'Admin' } },
  { path: 'users', component: UserListComponent, data: { title: 'Users' } },
];
