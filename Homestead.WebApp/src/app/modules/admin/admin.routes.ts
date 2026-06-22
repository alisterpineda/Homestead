import { Routes } from '@angular/router';
import { AdminIndexComponent } from './admin-index/admin-index.component';
import { UserListComponent } from './users/user-list.component';
import { GroupListComponent } from './groups/group-list.component';
import { GroupDetailPageComponent } from './groups/group-detail-page.component';
import { JobListComponent } from './jobs/job-list.component';
import { ScheduleListComponent } from './schedules/schedule-list.component';
import { SystemSettingsComponent } from './settings/system-settings.component';
import { AiStatusComponent } from './ai/ai-status.component';
export const ADMIN_ROUTES: Routes = [
  { path: '', component: AdminIndexComponent, data: { title: 'Admin' } },
  { path: 'users', component: UserListComponent, data: { title: 'Users' } },
  { path: 'groups', component: GroupListComponent, data: { title: 'Groups' } },
  { path: 'groups/:id', component: GroupDetailPageComponent, data: { title: 'Group' } },
  { path: 'jobs', component: JobListComponent, data: { title: 'Jobs' } },
  { path: 'schedules', component: ScheduleListComponent, data: { title: 'Schedules' } },
  { path: 'settings', component: SystemSettingsComponent, data: { title: 'System Settings' } },
  { path: 'ai', component: AiStatusComponent, data: { title: 'AI' } },
];
