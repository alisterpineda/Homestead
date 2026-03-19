import { Routes } from '@angular/router';
import { AdminIndexComponent } from './pages/admin-index/admin-index.component';
import { UserListComponent } from './pages/user-list/user-list.component';
import { GroupListComponent } from './pages/group-list/group-list.component';
import { GroupDetailPageComponent } from './pages/group-detail/group-detail-page.component';
import { JobListComponent } from './pages/job-list/job-list.component';
import { OcrEngineListComponent } from './pages/ocr-engine-list/ocr-engine-list.component';

export const ADMIN_ROUTES: Routes = [
  { path: '', component: AdminIndexComponent, data: { title: 'Admin' } },
  { path: 'users', component: UserListComponent, data: { title: 'Users' } },
  { path: 'groups', component: GroupListComponent, data: { title: 'Groups' } },
  { path: 'groups/:id', component: GroupDetailPageComponent, data: { title: 'Group' } },
  { path: 'jobs', component: JobListComponent, data: { title: 'Jobs' } },
  { path: 'ocr', component: OcrEngineListComponent, data: { title: 'Text Extraction Engines' } },
];
