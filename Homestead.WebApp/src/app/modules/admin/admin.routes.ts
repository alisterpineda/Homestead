import { Routes } from '@angular/router';
import { AdminIndexComponent } from './admin-index/admin-index.component';
import { UserListComponent } from './users/user-list.component';
import { GroupListComponent } from './groups/group-list.component';
import { GroupDetailPageComponent } from './groups/group-detail-page.component';
import { JobListComponent } from './jobs/job-list.component';
import { ScheduleListComponent } from './schedules/schedule-list.component';
import { TextExtractionEngineListComponent } from './text-extraction/text-extraction-engine-list.component';
import { AiProviderListComponent } from './ai-providers/ai-provider-list.component';
import { SystemSettingsComponent } from './settings/system-settings.component';
export const ADMIN_ROUTES: Routes = [
  { path: '', component: AdminIndexComponent, data: { title: 'Admin' } },
  { path: 'users', component: UserListComponent, data: { title: 'Users' } },
  { path: 'groups', component: GroupListComponent, data: { title: 'Groups' } },
  { path: 'groups/:id', component: GroupDetailPageComponent, data: { title: 'Group' } },
  { path: 'jobs', component: JobListComponent, data: { title: 'Jobs' } },
  { path: 'schedules', component: ScheduleListComponent, data: { title: 'Schedules' } },
  { path: 'text-extraction', component: TextExtractionEngineListComponent, data: { title: 'Text Extraction Engines' } },
  { path: 'ai-providers', component: AiProviderListComponent, data: { title: 'AI Providers' } },
  { path: 'settings', component: SystemSettingsComponent, data: { title: 'System Settings' } },
];
