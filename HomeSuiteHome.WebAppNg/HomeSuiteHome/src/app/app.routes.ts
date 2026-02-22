import { Routes } from '@angular/router';
import { AppShellComponent } from '@/shared/components/layout/app-shell/app-shell.component';

export const routes: Routes = [
  {
    path: '',
    component: AppShellComponent,
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
      },
    ],
  },
];
