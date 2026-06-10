import { Routes } from '@angular/router';
import { authGuard } from '@/shared/guards/auth.guard';
import { adminGuard } from '@/shared/guards/admin.guard';
import { loginGuard } from '@/shared/guards/login.guard';
import { pendingActivationGuard } from '@/shared/guards/pending-activation.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./shared/components/auth/login/login.component').then((m) => m.LoginComponent),
    canActivate: [loginGuard],
  },
  {
    path: 'pending-activation',
    loadComponent: () =>
      import('./shared/components/auth/pending-activation/pending-activation.component').then(
        (m) => m.PendingActivationComponent,
      ),
    canActivate: [pendingActivationGuard],
  },
  {
    path: '',
    loadComponent: () =>
      import('./shared/components/layout/app-shell/app-shell.component').then(
        (m) => m.AppShellComponent,
      ),
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'documents', pathMatch: 'full' },
      {
        path: 'documents',
        loadChildren: () =>
          import('./modules/documents/documents.routes').then((m) => m.DOCUMENTS_ROUTES),
      },
      {
        path: 'admin',
        loadChildren: () => import('./modules/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
        canActivate: [adminGuard],
      },
      {
        path: 'finances',
        loadChildren: () =>
          import('./modules/finances/finances.routes').then((m) => m.FINANCES_ROUTES),
      },
      {
        path: 'inventory',
        loadChildren: () =>
          import('./modules/inventory/inventory.routes').then((m) => m.INVENTORY_ROUTES),
      },
    ],
  },
];
