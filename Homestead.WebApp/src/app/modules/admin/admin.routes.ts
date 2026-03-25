import { Routes } from '@angular/router';
import { AdminIndexComponent } from './admin-index/admin-index.component';
import { UserListComponent } from './users/user-list.component';
import { GroupListComponent } from './groups/group-list.component';
import { GroupDetailPageComponent } from './groups/group-detail-page.component';
import { JobListComponent } from './jobs/job-list.component';
import { TextExtractionEngineListComponent } from './text-extraction/text-extraction-engine-list.component';
import { DocumentSettingsComponent } from './settings/document-settings.component';

export const ADMIN_ROUTES: Routes = [
  { path: '', component: AdminIndexComponent, data: { title: 'Admin' } },
  { path: 'users', component: UserListComponent, data: { title: 'Users' } },
  { path: 'groups', component: GroupListComponent, data: { title: 'Groups' } },
  { path: 'groups/:id', component: GroupDetailPageComponent, data: { title: 'Group' } },
  { path: 'jobs', component: JobListComponent, data: { title: 'Jobs' } },
  { path: 'text-extraction', component: TextExtractionEngineListComponent, data: { title: 'Text Extraction Engines' } },
  { path: 'settings/documents', component: DocumentSettingsComponent, data: { title: 'Document Settings' } },
];
